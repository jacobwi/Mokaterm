using System.Globalization;
using FluentFTP;
using FluentFTP.Exceptions;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Diagnostics;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Modules.Ftp.Connection;

namespace Mokaterm.Modules.Ftp.FileSystem;

/// <summary>
/// <see cref="IRemoteFileSystem"/> over FTP. Browsing commands share the session's control connection, one at a time
/// because FTP cannot run commands concurrently; transfers and recursive operations use connections from
/// <see cref="FtpClientPool"/>. The session owns this instance: disposing it does nothing, closing the session closes it.
/// </summary>
internal sealed class FtpFileSystem : IRemoteFileSystem
{
	// Finding out whether a link points at a folder costs a round trip per link.
	private const int MaxLinkProbes = 20;

	private const int FeatureUnknown = 0;
	private const int FeatureSupported = 1;
	private const int FeatureUnsupported = 2;

	private static readonly TimeSpan CloseWait = TimeSpan.FromSeconds(2);

	private static readonly RemoteFileEntry RootEntry = new() { Name = RemotePath.Root, Path = RemotePath.Root, Kind = RemoteEntryKind.Directory };

	private readonly Func<CancellationToken, Task<AsyncFtpClient>> _connectAsync;
	private readonly FtpClientPool _pool;
	private readonly SemaphoreSlim _gate = new(1, 1);
	private readonly TimeProvider _timeProvider;
	private readonly ILogger _logger;
	private readonly string _loginDirectory;
	private readonly string _homeDirectory;
	private readonly TimeSpan _dataTimeout;

	// Replaced when the connection breaks. Only used while holding _gate.
	private AsyncFtpClient _client;
	private long _lastActivity;
	private int _chmodSupport;
	private int _listAllFiles;
	private int _linksSeen;
	private int _closed;

	/// <param name="client">The connected browsing connection. The file system owns it from now on.</param>
	/// <param name="connectAsync">Opens another connection like <paramref name="client"/>, for transfers and reconnects.</param>
	/// <param name="maxTransferClients">How many transfer connections may be open at once.</param>
	/// <param name="dataTimeout">How long an upload waits for the server to take more data before it fails.</param>
	public FtpFileSystem(
		AsyncFtpClient client,
		Func<CancellationToken, Task<AsyncFtpClient>> connectAsync,
		Func<int> maxTransferClients,
		string userName,
		string loginDirectory,
		string homeDirectory,
		TimeSpan dataTimeout,
		TimeProvider timeProvider,
		ILogger logger)
	{
		_client = client;
		_connectAsync = connectAsync;
		UserName = userName;
		_loginDirectory = loginDirectory;
		_homeDirectory = homeDirectory;
		_dataTimeout = dataTimeout;
		_timeProvider = timeProvider;
		_logger = logger;
		_lastActivity = timeProvider.GetTimestamp();

		// IIS and other Windows servers have no SITE CHMOD; everywhere else it is tried on first use.
		_chmodSupport = client.ServerOS == FtpOperatingSystem.Windows ? FeatureUnsupported : FeatureUnknown;
		_pool = new FtpClientPool(connectAsync, maxTransferClients, LeaseSharedClientAsync, timeProvider, logger);
	}

	public RemoteFileSystemFeatures Features
	{
		get
		{
			RemoteFileSystemFeatures features = RemoteFileSystemFeatures.None;
			if (Volatile.Read(ref _chmodSupport) != FeatureUnsupported)
			{
				features |= RemoteFileSystemFeatures.Permissions;
			}

			if (Volatile.Read(ref _linksSeen) != 0)
			{
				features |= RemoteFileSystemFeatures.SymbolicLinks;
			}

			return features;
		}
	}

	public string UserName { get; }

	public bool IsElevated => false;

	public ValueTask<string> GetHomeDirectoryAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(_homeDirectory);

	public async ValueTask<string> ResolvePathAsync(string path, CancellationToken cancellationToken = default)
	{
		string lexical = FtpPaths.Resolve(path, _loginDirectory);
		if (RemotePath.IsRoot(lexical) || !FtpPaths.IsSendable(lexical))
		{
			return lexical;
		}

		return await RunAsync("resolve", lexical, RemoteFileErrorKind.NotFound, repeatable: true, async (client, token) =>
		{
			// PWD after entering a folder gives the server's own path for it, which resolves symbolic links on most servers.
			FtpReply entered = await client.Execute("CWD " + lexical, token);
			if (!entered.Success)
			{
				return lexical;
			}

			FtpReply current = await client.Execute("PWD", token);
			string? physical = current.Success ? FtpPaths.ParseQuotedPath(current.Message) : null;
			return !string.IsNullOrEmpty(physical) && physical[0] == RemotePath.Separator ? RemotePath.Normalize(physical) : lexical;
		}, cancellationToken);
	}

	public ValueTask<IReadOnlyList<RemoteFileEntry>> ListAsync(string path, CancellationToken cancellationToken = default)
	{
		string directory = RemotePath.Normalize(path);
		return RunAsync("list", directory, RemoteFileErrorKind.NotFound, repeatable: true, async (client, token) =>
		{
			IReadOnlyList<RemoteFileEntry> entries = await ListCoreAsync(client, directory, token);
			return await ProbeLinkTargetsAsync(client, entries, token);
		}, cancellationToken);
	}

	public ValueTask<RemoteFileEntry?> StatAsync(string path, CancellationToken cancellationToken = default)
	{
		string target = RemotePath.Normalize(path);
		return RemotePath.IsRoot(target)
			? ValueTask.FromResult<RemoteFileEntry?>(RootEntry)
			: RunAsync("read", target, RemoteFileErrorKind.NotFound, repeatable: true, (client, token) => StatCoreAsync(client, target, token), cancellationToken);
	}

	public async ValueTask CreateDirectoryAsync(string path, CancellationToken cancellationToken = default)
	{
		string target = RemotePath.Normalize(path);
		if (RemotePath.IsRoot(target))
		{
			throw AlreadyExists("create", target);
		}

		await RunAsync("create", target, RemoteFileErrorKind.PermissionDenied, repeatable: false, async (client, token) =>
		{
			// FluentFTP returns false when the folder is already there.
			return await client.CreateDirectory(target, force: false, token) ? true : throw AlreadyExists("create", target);
		}, cancellationToken);
	}

	public async ValueTask DeleteAsync(string path, bool recursive, CancellationToken cancellationToken = default)
	{
		string target = RemotePath.Normalize(path);
		if (RemotePath.IsRoot(target))
		{
			throw FtpErrors.Create(RemoteFileErrorKind.PermissionDenied, "delete", target, "the root folder cannot be deleted");
		}

		if (!recursive)
		{
			await RunAsync("delete", target, RemoteFileErrorKind.PermissionDenied, repeatable: false, async (client, token) =>
			{
				RemoteFileEntry entry = await StatCoreAsync(client, target, token) ?? throw NotFound("delete", target);
				await DeleteEntryAsync(client, entry, token);
				return true;
			}, cancellationToken);
			return;
		}

		// A folder tree can take thousands of commands; a transfer connection keeps browsing responsive meanwhile.
		await RunOnTransferClientAsync("delete", target, RemoteFileErrorKind.PermissionDenied, async (client, token) =>
		{
			RemoteFileEntry entry = await StatCoreAsync(client, target, token) ?? throw NotFound("delete", target);
			if (entry.Kind == RemoteEntryKind.Directory)
			{
				await DeleteTreeAsync(client, target, token);
			}
			else
			{
				await client.DeleteFile(target, token);
			}

			return true;
		}, cancellationToken);
	}

	public async ValueTask RenameAsync(string sourcePath, string destinationPath, bool overwrite, CancellationToken cancellationToken = default)
	{
		string source = RemotePath.Normalize(sourcePath);
		string destination = RemotePath.Normalize(destinationPath);
		if (source == destination)
		{
			return;
		}

		if (RemotePath.IsRoot(source))
		{
			throw FtpErrors.Create(RemoteFileErrorKind.PermissionDenied, "move", source, "the root folder cannot be moved");
		}

		if (RemotePath.IsSameOrInside(destination, source))
		{
			throw FtpErrors.Create(RemoteFileErrorKind.Unknown, "move", source, "a folder cannot be moved into itself");
		}

		await RunAsync("move", source, RemoteFileErrorKind.NotFound, repeatable: false, async (client, token) =>
		{
			// On a case-insensitive server the destination of a case-only rename is the source itself: never delete it.
			if (!string.Equals(source, destination, StringComparison.OrdinalIgnoreCase)
				&& await StatCoreAsync(client, destination, token) is { } existing)
			{
				if (!overwrite)
				{
					throw FtpErrors.Create(RemoteFileErrorKind.AlreadyExists, "move", source, $"{destination} already exists");
				}

				await DeleteEntryAsync(client, existing, token);
			}

			await client.Rename(source, destination, token);
			return true;
		}, cancellationToken);
	}

	public async ValueTask SetPermissionsAsync(string path, UnixFileMode permissions, bool recursive, CancellationToken cancellationToken = default)
	{
		string target = RemotePath.Normalize(path);
		if (Volatile.Read(ref _chmodSupport) == FeatureUnsupported)
		{
			throw ChmodNotSupported(target, null);
		}

		if (!recursive)
		{
			await RunAsync("change permissions of", target, RemoteFileErrorKind.NotFound, repeatable: true, async (client, token) =>
			{
				await ChmodAsync(client, target, permissions, token);
				return true;
			}, cancellationToken);
			return;
		}

		await RunOnTransferClientAsync("change permissions of", target, RemoteFileErrorKind.NotFound, async (client, token) =>
		{
			RemoteFileEntry entry = await StatCoreAsync(client, target, token) ?? throw NotFound("change permissions of", target);
			if (entry.Kind == RemoteEntryKind.Directory)
			{
				await ChmodTreeAsync(client, target, permissions, token);
			}

			await ChmodAsync(client, target, permissions, token);
			return true;
		}, cancellationToken);
	}

	public ValueTask SetOwnerAsync(string path, string owner, string? group, bool recursive, CancellationToken cancellationToken = default) =>
		ValueTask.FromException(FtpErrors.Create(RemoteFileErrorKind.NotSupported, "change the owner of", RemotePath.Normalize(path), "FTP has no command for it"));

	public async ValueTask<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default)
	{
		string source = RemotePath.Normalize(path);
		FtpClientLease lease = await LeaseAsync("open", source, cancellationToken);
		try
		{
			Stream remote = await lease.Client.OpenRead(source, FtpDataType.Binary, 0, checkIfFileExists: false, cancellationToken);
			return new FtpReadStream(remote, lease);
		}
		catch (Exception ex)
		{
			// A refused command leaves the connection in step with the server, so it stays reusable.
			await lease.ReleaseAsync(reusable: ex is FtpCommandException);
			if (IsCancellation(ex, cancellationToken))
			{
				throw;
			}

			throw FtpErrors.ToRemoteException(ex, "open", source, RemoteFileErrorKind.NotFound);
		}
	}

	public async ValueTask UploadAsync(string path, Stream source, UploadOptions options, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(options);
		string target = RemotePath.Normalize(path);
		FtpClientLease lease = await LeaseAsync("upload", target, cancellationToken);
		Stream? remote = null;
		bool reusable = false;
		try
		{
			if (!options.Overwrite && await lease.Client.FileExists(target, cancellationToken))
			{
				throw FtpErrors.Create(RemoteFileErrorKind.AlreadyExists, "upload", target, "a file with that name already exists");
			}

			remote = await lease.Client.OpenWrite(target, FtpDataType.Binary, checkIfFileExists: false, cancellationToken);
			await FtpDataStreams.CopyAsync(source, remote, progress, _dataTimeout, cancellationToken);
			Stream finished = remote;
			remote = null;
			await FtpDataStreams.FinishAsync(finished, cancellationToken);
			reusable = true;
			await ApplyUploadMetadataAsync(lease.Client, target, options, cancellationToken);
		}
		catch (Exception ex) when (!IsCancellation(ex, cancellationToken))
		{
			reusable = remote is null && ex is FtpCommandException or RemoteFileSystemException { Kind: RemoteFileErrorKind.AlreadyExists };
			throw FtpErrors.ToRemoteException(ex, "upload", target, RemoteFileErrorKind.PermissionDenied);
		}
		finally
		{
			await CompleteTransferAsync(lease, remote, reusable);
		}
	}

	public async ValueTask DownloadAsync(string path, Stream destination, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(destination);
		string source = RemotePath.Normalize(path);
		FtpClientLease lease = await LeaseAsync("download", source, cancellationToken);
		Stream? remote = null;
		bool reusable = false;
		try
		{
			remote = await lease.Client.OpenRead(source, FtpDataType.Binary, 0, checkIfFileExists: false, cancellationToken);

			// The reads from the server have FluentFTP's read timeout; the writes go to this machine, which may pause.
			await FtpDataStreams.CopyAsync(remote, destination, progress, writeTimeout: null, cancellationToken);
			Stream finished = remote;
			remote = null;
			await FtpDataStreams.FinishAsync(finished, cancellationToken);
			reusable = true;
		}
		catch (Exception ex) when (!IsCancellation(ex, cancellationToken))
		{
			reusable = remote is null && ex is FtpCommandException;
			throw FtpErrors.ToRemoteException(ex, "download", source, RemoteFileErrorKind.NotFound);
		}
		finally
		{
			await CompleteTransferAsync(lease, remote, reusable);
		}
	}

	/// <summary>The session owns the connections and closes them; disposing this view does nothing.</summary>
	public ValueTask DisposeAsync() => ValueTask.CompletedTask;

	/// <summary>
	/// Sends NOOP when the browsing connection has been idle for <paramref name="idleTime"/>, reconnecting when the server
	/// dropped it. Returns false when nothing was sent because the connection is busy or was used recently. Throws
	/// <see cref="IOException"/> when the server cannot be reached again.
	/// </summary>
	internal async Task<bool> KeepAliveAsync(TimeSpan idleTime, CancellationToken cancellationToken)
	{
		if (Volatile.Read(ref _closed) != 0 || _timeProvider.GetElapsedTime(Volatile.Read(ref _lastActivity)) < idleTime)
		{
			return false;
		}

		if (!await _gate.WaitAsync(TimeSpan.Zero, cancellationToken))
		{
			return false;
		}

		try
		{
			if (!await FtpClientFactory.IsAliveAsync(_client, cancellationToken))
			{
				await ReconnectAsync(cancellationToken);
			}

			return true;
		}
		catch (Exception ex) when (!IsCancellation(ex, cancellationToken))
		{
			throw new IOException("Could not reach the FTP server again.", ex);
		}
		finally
		{
			MarkActivity();
			_gate.Release();
		}
	}

	/// <summary>Closes the transfer connections and then the browsing connection.</summary>
	internal async ValueTask CloseAsync()
	{
		if (Interlocked.Exchange(ref _closed, 1) != 0)
		{
			return;
		}

		await _pool.DisposeAsync();

		// Let a running command finish so QUIT does not interleave with its reply; one that hangs is cut off.
		bool acquired = await _gate.WaitAsync(CloseWait);
		try
		{
			if (acquired)
			{
				await FtpClientFactory.CloseAsync(_client);
			}
			else
			{
				await FtpClientFactory.DiscardAsync(_client);
			}
		}
		finally
		{
			if (acquired)
			{
				_gate.Release();
			}
		}
	}

	private static RemoteFileSystemException NotFound(string action, string path) =>
		FtpErrors.Create(RemoteFileErrorKind.NotFound, action, path, "it does not exist");

	private static RemoteFileSystemException AlreadyExists(string action, string path) =>
		FtpErrors.Create(RemoteFileErrorKind.AlreadyExists, action, path, "it already exists");

	private static RemoteFileSystemException ChmodNotSupported(string path, Exception? innerException) =>
		FtpErrors.Create(RemoteFileErrorKind.NotSupported, "change permissions of", path, "the server does not support SITE CHMOD", innerException);

	private static bool IsCancellation(Exception exception, CancellationToken cancellationToken) =>
		exception is OperationCanceledException && cancellationToken.IsCancellationRequested;

	private static async Task<bool> DirectoryExistsAsync(AsyncFtpClient client, string path, CancellationToken cancellationToken)
	{
		if (!FtpPaths.IsSendable(path))
		{
			return false;
		}

		FtpReply reply = await client.Execute("CWD " + path, cancellationToken);
		return reply.Success;
	}

	private static async Task<IReadOnlyList<RemoteFileEntry>> ProbeLinkTargetsAsync(AsyncFtpClient client, IReadOnlyList<RemoteFileEntry> entries, CancellationToken cancellationToken)
	{
		List<RemoteFileEntry>? resolved = null;
		int probes = 0;
		for (int i = 0; i < entries.Count && probes < MaxLinkProbes; i++)
		{
			RemoteFileEntry entry = entries[i];
			if (entry.Kind != RemoteEntryKind.SymbolicLink || entry.LinkTargetKind is not null)
			{
				continue;
			}

			probes++;
			if (await ProbeLinkTargetAsync(client, entry.Path, cancellationToken) is { } targetKind)
			{
				resolved ??= [.. entries];
				resolved[i] = entry with { LinkTargetKind = targetKind };
			}
		}

		return resolved ?? entries;
	}

	private static async Task<RemoteEntryKind?> ProbeLinkTargetAsync(AsyncFtpClient client, string path, CancellationToken cancellationToken)
	{
		if (!FtpPaths.IsSendable(path))
		{
			return null;
		}

		// Entering a link only works when it points at a folder. A refusal usually means a file, though a broken link
		// or one the user cannot enter looks the same.
		FtpReply reply = await client.Execute("CWD " + path, cancellationToken);
		if (reply.Success)
		{
			return RemoteEntryKind.Directory;
		}

		return reply.Code is { Length: > 0 } code && code[0] == '5' ? RemoteEntryKind.File : null;
	}

	private static async Task CompleteTransferAsync(FtpClientLease lease, Stream? remote, bool reusable)
	{
		if (remote is null)
		{
			await lease.ReleaseAsync(reusable);
			return;
		}

		await FtpDataStreams.AbandonAsync(remote, lease);
	}

	/// <summary>Runs a browsing command on the shared connection, one at a time.</summary>
	/// <param name="repeatable">
	/// True for commands that change nothing or give the same result when run twice. Those are repeated once on a fresh
	/// connection when the first attempt got no answer; the others only benefit from the idle check before they start.
	/// </param>
	private async ValueTask<T> RunAsync<T>(
		string action,
		string path,
		RemoteFileErrorKind unavailable,
		bool repeatable,
		Func<AsyncFtpClient, CancellationToken, Task<T>> operation,
		CancellationToken cancellationToken)
	{
		ThrowIfClosed(action, path);
		await _gate.WaitAsync(cancellationToken);
		try
		{
			ThrowIfClosed(action, path);
			AsyncFtpClient client = await GetConnectedClientAsync(cancellationToken);
			try
			{
				return await operation(client, cancellationToken);
			}
			catch (Exception ex) when (repeatable && FtpErrors.IsNoAnswer(ex))
			{
				// FluentFTP only notices a dropped connection once a command fails on it.
				await ReconnectAsync(cancellationToken);
				return await operation(_client, cancellationToken);
			}
		}
		catch (Exception ex) when (!IsCancellation(ex, cancellationToken))
		{
			throw FtpErrors.ToRemoteException(ex, action, path, unavailable);
		}
		finally
		{
			MarkActivity();
			_gate.Release();
		}
	}

	// Call with _gate held.
	private async Task<AsyncFtpClient> GetConnectedClientAsync(CancellationToken cancellationToken)
	{
		// A connection used moments ago is trusted; one that sat idle is checked first, so a command that must not run
		// twice is never sent into a connection the server already dropped.
		bool recentlyUsed = _timeProvider.GetElapsedTime(Volatile.Read(ref _lastActivity)) < FtpClientPool.ProbeAfterIdle;
		if (_client.IsConnected && (recentlyUsed || await FtpClientFactory.IsAliveAsync(_client, cancellationToken)))
		{
			return _client;
		}

		await ReconnectAsync(cancellationToken);
		return _client;
	}

	// Call with _gate held.
	private async Task ReconnectAsync(CancellationToken cancellationToken)
	{
		AsyncFtpClient replacement = await _connectAsync(cancellationToken);
		AsyncFtpClient broken = _client;
		_client = replacement;
		await FtpClientFactory.DiscardAsync(broken);
		_logger.LogInformation("Reconnected to the FTP server after the connection dropped");
	}

	private async ValueTask<T> RunOnTransferClientAsync<T>(
		string action,
		string path,
		RemoteFileErrorKind unavailable,
		Func<AsyncFtpClient, CancellationToken, Task<T>> operation,
		CancellationToken cancellationToken)
	{
		FtpClientLease lease = await LeaseAsync(action, path, cancellationToken);
		bool reusable = false;
		try
		{
			T result = await operation(lease.Client, cancellationToken);
			reusable = true;
			return result;
		}
		catch (Exception ex) when (!IsCancellation(ex, cancellationToken))
		{
			reusable = ex is FtpCommandException or RemoteFileSystemException { Kind: not RemoteFileErrorKind.ConnectionLost };
			throw FtpErrors.ToRemoteException(ex, action, path, unavailable);
		}
		finally
		{
			await lease.ReleaseAsync(reusable);
		}
	}

	private async ValueTask<FtpClientLease> LeaseAsync(string action, string path, CancellationToken cancellationToken)
	{
		ThrowIfClosed(action, path);
		try
		{
			return await _pool.LeaseAsync(cancellationToken);
		}
		catch (Exception ex) when (!IsCancellation(ex, cancellationToken))
		{
			throw FtpErrors.ToRemoteException(ex, action, path, RemoteFileErrorKind.Unknown);
		}
	}

	private async ValueTask<FtpClientLease> LeaseSharedClientAsync(CancellationToken cancellationToken)
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			ObjectDisposedException.ThrowIf(Volatile.Read(ref _closed) != 0, this);
			return new FtpClientLease(await GetConnectedClientAsync(cancellationToken), isShared: true, ReleaseSharedClientAsync);
		}
		catch
		{
			_gate.Release();
			throw;
		}
	}

	private ValueTask ReleaseSharedClientAsync(FtpClientLease lease, bool reusable)
	{
		MarkActivity();
		_gate.Release();
		return ValueTask.CompletedTask;
	}

	private async Task<IReadOnlyList<RemoteFileEntry>> ListCoreAsync(AsyncFtpClient client, string directory, CancellationToken cancellationToken)
	{
		FtpListItem[] items = await GetListingAsync(client, directory, cancellationToken);
		List<RemoteFileEntry> entries = new(items.Length);
		foreach (FtpListItem item in items)
		{
			if (FtpEntryMapper.IsListable(item))
			{
				entries.Add(FtpEntryMapper.ToEntry(item, RemotePath.Combine(directory, item.Name)));
			}
		}

		// FluentFTP turns a 550 for LIST into an empty listing, because some servers answer an empty folder that way.
		if (entries.Count == 0 && !RemotePath.IsRoot(directory) && !await DirectoryExistsAsync(client, directory, cancellationToken))
		{
			throw FtpErrors.Create(RemoteFileErrorKind.NotFound, "list", directory, "the folder does not exist or cannot be opened");
		}

		if (entries.Exists(entry => entry.Kind == RemoteEntryKind.SymbolicLink))
		{
			Volatile.Write(ref _linksSeen, 1);
		}

		return entries;
	}

	private async Task<FtpListItem[]> GetListingAsync(AsyncFtpClient client, string directory, CancellationToken cancellationToken)
	{
		// Hidden files need "LIST -a", which not every server understands. Machine listings (MLSD) include them anyway.
		if (client.HasFeature(FtpCapability.MLST) || Volatile.Read(ref _listAllFiles) == FeatureUnsupported)
		{
			return await client.GetListing(directory, FtpListOption.Auto, cancellationToken);
		}

		FtpListItem[] items;
		try
		{
			items = await client.GetListing(directory, FtpListOption.AllFiles, cancellationToken);
		}
		catch (FtpCommandException ex) when (ex.CompletionCode is "500" or "501" or "502" or "504")
		{
			items = [];
		}

		if (items.Length > 0)
		{
			Volatile.Write(ref _listAllFiles, FeatureSupported);
			return items;
		}

		if (Volatile.Read(ref _listAllFiles) == FeatureSupported)
		{
			return items;
		}

		// Nothing came back, which is also what a server that took "-a" for a file name returns.
		FtpListItem[] plain = await client.GetListing(directory, FtpListOption.Auto, cancellationToken);
		if (plain.Length > 0)
		{
			Volatile.Write(ref _listAllFiles, FeatureUnsupported);
			_logger.LogDebug("The FTP server does not understand LIST -a; listings may miss hidden files");
		}

		return plain;
	}

	private async Task<RemoteFileEntry?> StatCoreAsync(AsyncFtpClient client, string path, CancellationToken cancellationToken)
	{
		if (RemotePath.IsRoot(path))
		{
			return RootEntry;
		}

		if (client.HasFeature(FtpCapability.MLST))
		{
			FtpListItem? item = await client.GetObjectInfo(path, dateModified: false, cancellationToken);
			if (item is not null)
			{
				return await WithLinkTargetAsync(client, FtpEntryMapper.ToEntry(item, path), cancellationToken);
			}

			// 550 means there is nothing to see at the path. Other outcomes, such as a reply FluentFTP could not parse,
			// fall back to the parent's listing.
			if (client.LastReply.Code == "550")
			{
				return null;
			}
		}

		string name = RemotePath.GetName(path);
		try
		{
			foreach (FtpListItem item in await GetListingAsync(client, RemotePath.GetParent(path), cancellationToken))
			{
				if (FtpEntryMapper.IsListable(item) && string.Equals(item.Name, name, StringComparison.Ordinal))
				{
					return await WithLinkTargetAsync(client, FtpEntryMapper.ToEntry(item, path), cancellationToken);
				}
			}
		}
		catch (FtpCommandException ex) when (ex.CompletionCode is { Length: > 0 } code && code[0] == '5')
		{
			// The parent cannot be listed, but the path itself may still be a folder the user can enter.
		}

		return await DirectoryExistsAsync(client, path, cancellationToken)
			? new RemoteFileEntry { Name = name, Path = path, Kind = RemoteEntryKind.Directory }
			: null;
	}

	private static async Task<RemoteFileEntry> WithLinkTargetAsync(AsyncFtpClient client, RemoteFileEntry entry, CancellationToken cancellationToken) =>
		entry.Kind == RemoteEntryKind.SymbolicLink && entry.LinkTargetKind is null
			? entry with { LinkTargetKind = await ProbeLinkTargetAsync(client, entry.Path, cancellationToken) }
			: entry;

	private async Task DeleteEntryAsync(AsyncFtpClient client, RemoteFileEntry entry, CancellationToken cancellationToken)
	{
		if (entry.Kind == RemoteEntryKind.Directory)
		{
			await RemoveDirectoryAsync(client, entry.Path, cancellationToken);
		}
		else
		{
			// Links are deleted as links; their targets stay.
			await client.DeleteFile(entry.Path, cancellationToken);
		}
	}

	private async Task DeleteTreeAsync(AsyncFtpClient client, string directory, CancellationToken cancellationToken)
	{
		foreach (RemoteFileEntry child in await ListCoreAsync(client, directory, cancellationToken))
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (child.Kind == RemoteEntryKind.Directory)
			{
				await DeleteTreeAsync(client, child.Path, cancellationToken);
			}
			else
			{
				await client.DeleteFile(child.Path, cancellationToken);
			}
		}

		await RemoveDirectoryAsync(client, directory, cancellationToken);
	}

	private async Task RemoveDirectoryAsync(AsyncFtpClient client, string path, CancellationToken cancellationToken)
	{
		if (!FtpPaths.IsSendable(path))
		{
			throw FtpErrors.Create(RemoteFileErrorKind.Unknown, "delete", path, "the path contains characters that cannot be sent over FTP");
		}

		// FluentFTP's DeleteDirectory always removes the contents too, so a plain folder delete sends RMD itself.
		FtpReply reply = await client.Execute("RMD " + path, cancellationToken);
		if (!reply.Success && reply.Code == "550" && (await client.Execute("CWD " + RemotePath.GetParent(path), cancellationToken)).Success)
		{
			// Some servers (IIS) refuse to remove the session's current folder, which a listing check may have entered.
			reply = await client.Execute("RMD " + path, cancellationToken);
		}

		if (reply.Success)
		{
			return;
		}

		RemoteFileErrorKind kind = FtpErrors.ClassifyReply(reply.Code, reply.Message, RemoteFileErrorKind.PermissionDenied);
		if (kind == RemoteFileErrorKind.PermissionDenied && reply.Code == "550" && await HasEntriesAsync(client, path, cancellationToken))
		{
			kind = RemoteFileErrorKind.DirectoryNotEmpty;
		}

		throw FtpErrors.Create(kind, "delete", path, FtpErrors.DescribeReply(reply.Code, reply.Message));
	}

	private async Task<bool> HasEntriesAsync(AsyncFtpClient client, string directory, CancellationToken cancellationToken)
	{
		try
		{
			return (await ListCoreAsync(client, directory, cancellationToken)).Count > 0;
		}
		catch (Exception ex) when (ex is FtpCommandException or RemoteFileSystemException)
		{
			return false;
		}
	}

	private async Task ChmodTreeAsync(AsyncFtpClient client, string directory, UnixFileMode permissions, CancellationToken cancellationToken)
	{
		foreach (RemoteFileEntry child in await ListCoreAsync(client, directory, cancellationToken))
		{
			cancellationToken.ThrowIfCancellationRequested();

			// SITE CHMOD on a link changes its target on most servers, which may lie outside the tree.
			if (child.Kind == RemoteEntryKind.SymbolicLink)
			{
				continue;
			}

			if (child.Kind == RemoteEntryKind.Directory)
			{
				await ChmodTreeAsync(client, child.Path, permissions, cancellationToken);
			}

			await ChmodAsync(client, child.Path, permissions, cancellationToken);
		}
	}

	private async Task ChmodAsync(AsyncFtpClient client, string path, UnixFileMode permissions, CancellationToken cancellationToken)
	{
		// SITE CHMOD takes the octal digits as written, such as 755 or 2775.
		int mode = int.Parse(UnixFileModeFormat.ToOctal(permissions), NumberStyles.None, CultureInfo.InvariantCulture);
		try
		{
			await client.SetFilePermissions(path, mode, cancellationToken);
			Interlocked.CompareExchange(ref _chmodSupport, FeatureSupported, FeatureUnknown);
		}
		catch (FtpCommandException ex) when (FtpErrors.IsNotImplemented(ex.CompletionCode))
		{
			Volatile.Write(ref _chmodSupport, FeatureUnsupported);
			throw ChmodNotSupported(path, ex);
		}
	}

	private async Task ApplyUploadMetadataAsync(AsyncFtpClient client, string path, UploadOptions options, CancellationToken cancellationToken)
	{
		if (options.Permissions is { } permissions && Volatile.Read(ref _chmodSupport) != FeatureUnsupported)
		{
			try
			{
				await ChmodAsync(client, path, permissions, cancellationToken);
			}
			catch (Exception ex) when (!IsCancellation(ex, cancellationToken))
			{
				_logger.LogWarning("Uploaded a file but could not set its permissions: {Error}", LogSafe.Describe(ex));
			}
		}

		// Only MFMT is standard for setting a modification time. FluentFTP would otherwise send an MDTM form that some
		// servers read as a query for a file named after the timestamp.
		if (options.LastModified is { } lastModified && client.HasFeature(FtpCapability.MFMT))
		{
			try
			{
				await client.SetModifiedTime(path, lastModified.UtcDateTime, cancellationToken);
			}
			catch (Exception ex) when (!IsCancellation(ex, cancellationToken))
			{
				_logger.LogWarning("Uploaded a file but could not set its modification time: {Error}", LogSafe.Describe(ex));
			}
		}
	}

	private void ThrowIfClosed(string action, string path)
	{
		if (Volatile.Read(ref _closed) != 0)
		{
			throw FtpErrors.Create(RemoteFileErrorKind.ConnectionLost, action, path, "the FTP session is closed");
		}
	}

	private void MarkActivity() => Volatile.Write(ref _lastActivity, _timeProvider.GetTimestamp());
}
