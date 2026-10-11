using System.Buffers;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Diagnostics;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Modules.Ssh.Connection;
using Mokaterm.Modules.Ssh.Shell;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;

namespace Mokaterm.Modules.Ssh.FileSystem;

internal sealed record SftpFileSystemOptions
{
	public required string UserName { get; init; }

	/// <summary>Where the browser starts instead of the home directory; may start with <c>~</c>.</summary>
	public string? InitialDirectory { get; init; }

	public bool SupportsElevation { get; init; }
}

/// <summary>
/// The logged-in user's files over SFTP. Calls into the SSH.NET client go one at a time through an async lock; long
/// transfers take the lock per chunk so browsing stays responsive while they run.
/// </summary>
/// <remarks>
/// SSH.NET resolves nearly every path with the server's <c>realpath</c> before using it, which follows symbolic links.
/// Operations that must act on a link itself (stat, delete, rename, recursive walks) therefore locate the entry through
/// its parent directory and use the path SSH.NET reports for it.
/// </remarks>
internal sealed class SftpFileSystem : IRemoteFileSystem
{
	private const int TransferBufferSize = 128 * 1024;
	private const int LinkBatchSize = 200;

	private const UnixFileMode PermissionBits =
		UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
		| UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
		| UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

	private readonly SftpClient _client;
	private readonly SftpFileSystemOptions _options;
	private readonly RemoteAccountNames _accountNames;
	private readonly Func<SshClient?> _shell;
	private readonly ILogger _logger;
	private readonly SemaphoreSlim _gate = new(1, 1);
	private string? _startDirectory;
	private bool _shellLinksUnavailable;
	private int _checksumUnavailable;
	private int _commandsUnavailable;
	private int _closed;

	/// <param name="shell">The session's connected SSH client when there is one; used to read link targets in one round trip.</param>
	public SftpFileSystem(SftpClient client, SftpFileSystemOptions options, RemoteAccountNames accountNames, Func<SshClient?> shell, ILogger logger)
	{
		_client = client;
		_options = options;
		_accountNames = accountNames;
		_shell = shell;
		_logger = logger;
	}

	// Accounts are read with a command, so an account limited to SFTP gets an empty list rather than none at all. Copies
	// and archives are commands with nothing to fall back on, so they are only offered while commands can run.
	public RemoteFileSystemFeatures Features =>
		RemoteFileSystemFeatures.Permissions | RemoteFileSystemFeatures.Ownership | RemoteFileSystemFeatures.SymbolicLinks
		| RemoteFileSystemFeatures.Accounts
		| (_options.SupportsElevation ? RemoteFileSystemFeatures.Elevation : RemoteFileSystemFeatures.None)
		| (CanRunCommands ? RemoteFileSystemFeatures.Copy | RemoteFileSystemFeatures.Archives : RemoteFileSystemFeatures.None);

	public string UserName => _options.UserName;

	public bool IsElevated => false;

	/// <summary>False once the session closed this file system or its connection dropped.</summary>
	public bool IsUsable => Volatile.Read(ref _closed) == 0 && ConnectionMonitor.IsConnected(_client);

	public string HomeDirectory
	{
		get
		{
			ThrowIfClosed();
			try
			{
				string workingDirectory = _client.WorkingDirectory;
				return string.IsNullOrEmpty(workingDirectory) ? RemotePath.Root : workingDirectory;
			}
			catch (Exception ex) when (RemoteFileErrors.TryMap(ex, null, out RemoteFileSystemException? mapped))
			{
				throw mapped;
			}
		}
	}

	public async ValueTask<string> GetHomeDirectoryAsync(CancellationToken cancellationToken = default)
	{
		if (_startDirectory is not null)
		{
			return _startDirectory;
		}

		string start = HomeDirectory;
		if (_options.InitialDirectory is { } initialDirectory)
		{
			try
			{
				string resolved = await ResolvePathAsync(initialDirectory, cancellationToken);
				if (await StatAsync(resolved, cancellationToken) is { IsDirectoryLike: true })
				{
					start = resolved;
				}
			}
			catch (RemoteFileSystemException ex) when (ex.Kind is not RemoteFileErrorKind.ConnectionLost)
			{
				_logger.LogDebug("The initial directory is not usable; starting in the home directory: {Error}", LogSafe.Describe(ex));
			}
		}

		return _startDirectory = start;
	}

	public async ValueTask<string> ResolvePathAsync(string path, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(path);
		string absolute = ExpandHome(path, HomeDirectory);
		ISftpFile file = await RunAsync(absolute, (client, token) => client.GetAsync(absolute, token), cancellationToken);
		return file.FullName;
	}

	public async ValueTask<IReadOnlyList<RemoteFileEntry>> ListAsync(string path, CancellationToken cancellationToken = default)
	{
		ThrowIfClosed();
		string directory = RemotePath.Normalize(path);
		AccountDirectory accounts = await _accountNames.GetAsync(cancellationToken);
		SshClient? shell = _shellLinksUnavailable ? null : _shell();

		List<RemoteFileEntry> entries = await RunAsync(directory, async (client, token) =>
		{
			List<RemoteFileEntry> listed = [];
			await foreach (ISftpFile file in client.ListDirectoryAsync(directory, token))
			{
				if (file.Name is not ("." or "..") && RemotePath.IsValidName(file.Name))
				{
					listed.Add(ToEntry(RemotePath.Combine(directory, file.Name), file.Attributes, accounts));
				}
			}

			if (shell is null)
			{
				await ResolveLinksOverSftpAsync(client, listed, token);
			}

			return listed;
		}, cancellationToken);

		if (shell is not null && entries.Exists(entry => entry.Kind == RemoteEntryKind.SymbolicLink))
		{
			await ResolveLinksOverShellAsync(shell, directory, entries, cancellationToken);
		}

		return entries;
	}

	public async ValueTask<RemoteFileEntry?> StatAsync(string path, CancellationToken cancellationToken = default)
	{
		ThrowIfClosed();
		string target = RemotePath.Normalize(path);
		AccountDirectory accounts = await _accountNames.GetAsync(cancellationToken);
		return await RunAsync<RemoteFileEntry?>(target, async (client, token) =>
		{
			if (await LocateAsync(client, target, token) is not { } file)
			{
				return null;
			}

			RemoteFileEntry entry = ToEntry(target, file.Attributes, accounts);
			return entry.Kind == RemoteEntryKind.SymbolicLink ? await WithSftpLinkTargetAsync(client, entry, token) : entry;
		}, cancellationToken);
	}

	public async ValueTask CreateDirectoryAsync(string path, CancellationToken cancellationToken = default)
	{
		string target = RemotePath.Normalize(path);
		await RunAsync(target, async (client, token) =>
		{
			try
			{
				await client.CreateDirectoryAsync(target, token);
			}
			catch (SftpException ex) when (ex is not (SftpPathNotFoundException or SftpPermissionDeniedException))
			{
				// SFTP version 3 has no "already exists" status, so look before reporting a generic failure.
				if (await LocateAsync(client, target, token) is not null)
				{
					throw RemoteFileErrors.AlreadyExists(target);
				}

				throw;
			}

			return true;
		}, cancellationToken);
	}

	public async ValueTask DeleteAsync(string path, bool recursive, CancellationToken cancellationToken = default)
	{
		string target = RemotePath.Normalize(path);
		if (RemotePath.IsRoot(target))
		{
			throw new RemoteFileSystemException(RemoteFileErrorKind.PermissionDenied, "The root directory cannot be deleted.", target);
		}

		ISftpFile entry = await RunAsync(target, (client, token) => LocateAsync(client, target, token), cancellationToken)
			?? throw RemoteFileErrors.NotFound(target);

		if (IsRealDirectory(entry) && recursive)
		{
			await DeleteTreeAsync(entry, cancellationToken);
			return;
		}

		await RunAsync(target, async (client, token) =>
		{
			try
			{
				await entry.DeleteAsync(token);
			}
			catch (SftpException ex) when (IsRealDirectory(entry) && ex is not (SftpPathNotFoundException or SftpPermissionDeniedException))
			{
				if (await HasChildrenAsync(client, entry.FullName, token))
				{
					throw new RemoteFileSystemException(RemoteFileErrorKind.DirectoryNotEmpty, $"{target} is not empty.", target, ex);
				}

				throw;
			}

			return true;
		}, cancellationToken);
	}

	public async ValueTask RenameAsync(string sourcePath, string destinationPath, bool overwrite, CancellationToken cancellationToken = default)
	{
		string source = RemotePath.Normalize(sourcePath);
		string destination = RemotePath.Normalize(destinationPath);
		if (string.Equals(source, destination, StringComparison.Ordinal))
		{
			return;
		}

		await RunAsync(source, async (client, token) =>
		{
			ISftpFile entry = await LocateAsync(client, source, token) ?? throw RemoteFileErrors.NotFound(source);
			string destinationParent = RemotePath.GetParent(destination);
			ISftpFile parent = await GetOrNullAsync(client, destinationParent, token) ?? throw RemoteFileErrors.NotFound(destinationParent);
			string rawDestination = RemotePath.Combine(parent.FullName, RemotePath.GetName(destination));

			if (await LocateAsync(client, destination, token) is { } existing)
			{
				if (!overwrite)
				{
					throw RemoteFileErrors.AlreadyExists(destination);
				}

				if (entry.Attributes.IsRegularFile && existing.Attributes.IsRegularFile && await TryPosixRenameAsync(client, entry.FullName, rawDestination, token))
				{
					return true;
				}

				await DeleteExistingAsync(client, existing, destination, token);
			}

			if (entry.IsSymbolicLink)
			{
				// RenameFileAsync would resolve the link and move its target instead.
				await Task.Run(() => entry.MoveTo(rawDestination), token);
			}
			else
			{
				await client.RenameFileAsync(entry.FullName, rawDestination, token);
			}

			return true;
		}, cancellationToken);
	}

	public ValueTask SetPermissionsAsync(string path, UnixFileMode permissions, bool recursive, CancellationToken cancellationToken = default) =>
		ChangePermissionsAsync(path, new PermissionChange { Mode = permissions, Recursive = recursive }, cancellationToken);

	public async ValueTask ChangePermissionsAsync(string path, PermissionChange change, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(change);
		string target = RemotePath.Normalize(path);
		ISftpFile entry = await RunAsync(target, (client, token) => LocateAsync(client, target, token), cancellationToken)
			?? throw RemoteFileErrors.NotFound(target);

		if (entry.IsSymbolicLink)
		{
			// setstat follows the link, and the link's own mode (always 777) says nothing about what it points at.
			if (change.Includes(RemoteEntryKind.SymbolicLink))
			{
				await SetAttributesAsync(
					target,
					attributes => UnixModes.ApplyTo(attributes, change.Apply(UnixModes.FromAttributes(attributes), attributes.IsDirectory)),
					cancellationToken);
			}

			return;
		}

		await ApplyAttributesAsync(
			entry,
			change.Recursive,
			file =>
			{
				if (!change.Includes(UnixModes.KindOf(file.Attributes)))
				{
					return false;
				}

				UnixModes.ApplyTo(file.Attributes, change.Apply(UnixModes.FromAttributes(file.Attributes), IsRealDirectory(file)));
				return true;
			},
			cancellationToken);
	}

	public async ValueTask SetOwnerAsync(string path, string owner, string? group, bool recursive, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(owner);
		ThrowIfClosed();
		string target = RemotePath.Normalize(path);
		AccountDirectory accounts = await _accountNames.GetAsync(cancellationToken);
		int userId = accounts.UserId(owner) ?? throw UnknownAccount(accounts, "user", owner, target);
		int? groupId = group is null ? null : accounts.GroupId(group) ?? throw UnknownAccount(accounts, "group", group, target);

		ISftpFile entry = await RunAsync(target, (client, token) => LocateAsync(client, target, token), cancellationToken)
			?? throw RemoteFileErrors.NotFound(target);

		await ApplyAttributesAsync(
			entry,
			recursive,
			file =>
			{
				file.Attributes.UserId = userId;
				if (groupId is { } id)
				{
					file.Attributes.GroupId = id;
				}

				return true;
			},
			cancellationToken);
	}

	public async ValueTask SetGroupAsync(string path, string group, bool recursive, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(group);
		ThrowIfClosed();
		string target = RemotePath.Normalize(path);
		AccountDirectory accounts = await _accountNames.GetAsync(cancellationToken);
		int groupId = accounts.GroupId(group) ?? throw UnknownAccount(accounts, "group", group, target);

		ISftpFile entry = await RunAsync(target, (client, token) => LocateAsync(client, target, token), cancellationToken)
			?? throw RemoteFileErrors.NotFound(target);

		// SFTP sets uid and gid together; each entry sends its own current uid back, so owners stay as they are.
		await ApplyAttributesAsync(
			entry,
			recursive,
			file =>
			{
				file.Attributes.GroupId = groupId;
				return true;
			},
			cancellationToken);
	}

	public async ValueTask<RemoteAccounts> ListAccountsAsync(CancellationToken cancellationToken = default)
	{
		ThrowIfClosed();
		return (await _accountNames.GetAsync(cancellationToken)).Accounts;
	}

	public async ValueTask<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default)
	{
		string target = RemotePath.Normalize(path);
		return new SftpReadStream(await OpenAtAsync(target, 0, cancellationToken), _gate, target);
	}

	public async ValueTask UploadAsync(string path, Stream source, UploadOptions options, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(options);
		string target = RemotePath.Normalize(path);

		if (options.Resume is { } resume && await TryUploadResumableAsync(target, source, options, resume, progress, cancellationToken))
		{
			return;
		}

		// Writing in place truncates the old file before a byte of the new one arrived, and a failure then deletes what is
		// left. A temporary file beside it takes its place only once the upload is complete.
		if (options.Overwrite && await TryOpenReplacementAsync(target, options.Permissions, cancellationToken) is { } replacement)
		{
			await PlaceAsync(replacement, source, options, resume: null, replace: true, progress, cancellationToken);
			return;
		}

		SftpFileStream stream = await RunAsync(target, async (client, token) =>
		{
			try
			{
				return await client.OpenAsync(target, options.Overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, token);
			}
			catch (SftpException ex) when (!options.Overwrite && ex is not (SftpPathNotFoundException or SftpPermissionDeniedException))
			{
				if (await LocateAsync(client, target, token) is not null)
				{
					throw RemoteFileErrors.AlreadyExists(target);
				}

				throw;
			}
		}, cancellationToken);

		bool written = false;
		try
		{
			if (options.Permissions is { } permissions)
			{
				// Set before any data arrives, so a private file is never readable under the server's default mode.
				await SetAttributesAsync(target, attributes => UnixModes.ApplyTo(attributes, permissions), cancellationToken);
			}

			await CopyToRemoteAsync(source, stream, target, progress, cancellationToken);
			written = true;
		}
		finally
		{
			await CloseUploadAsync(stream, target, written);
		}

		if (options.LastModified is { } lastModified)
		{
			await SetAttributesAsync(target, attributes => attributes.LastWriteTimeUtc = lastModified.UtcDateTime, cancellationToken);
		}
	}

	public ValueTask DownloadAsync(string path, Stream destination, IProgress<long>? progress = null, CancellationToken cancellationToken = default) =>
		DownloadAsync(path, destination, 0, progress, cancellationToken);

	public async ValueTask DownloadAsync(string path, Stream destination, long offset, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(destination);
		ArgumentOutOfRangeException.ThrowIfNegative(offset);
		string target = RemotePath.Normalize(path);
		Stream source = new SftpReadStream(await OpenAtAsync(target, offset, cancellationToken), _gate, target);
		await using (source)
		{
			byte[] buffer = ArrayPool<byte>.Shared.Rent(TransferBufferSize);
			try
			{
				long total = 0;
				int read;
				while ((read = await source.ReadAsync(buffer.AsMemory(0, TransferBufferSize), cancellationToken)) > 0)
				{
					await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
					total += read;
					progress?.Report(total);
				}
			}
			finally
			{
				ArrayPool<byte>.Shared.Return(buffer);
			}
		}
	}

	// The checksum runs as a command on the session's SSH connection, which an SFTP-only session does not have.
	public ChecksumAlgorithm? ServerChecksum =>
		Volatile.Read(ref _checksumUnavailable) == 0 && _shell() is not null ? ChecksumAlgorithm.Sha256 : null;

	public async ValueTask<string?> ComputeChecksumAsync(string path, CancellationToken cancellationToken = default)
	{
		string target = RemotePath.Normalize(path);
		if (Volatile.Read(ref _checksumUnavailable) != 0 || _shell() is not { } shell)
		{
			return null;
		}

		RemoteCommandResult result;
		try
		{
			result = await RemoteCommand.RunAsync(shell, PosixShell.ScriptCommand(RemoteScripts.Checksum, [target]), null, cancellationToken);
		}
		catch (Exception ex) when (RemoteFileErrors.TryMap(ex, target, out RemoteFileSystemException? mapped))
		{
			throw mapped;
		}

		// No marker: an account limited to SFTP runs no commands. Exit 105: no SHA-256 tool. Neither changes later.
		if (!RemoteOutput.TryGetPayload(result.Output, out ReadOnlyMemory<byte> payload) || result.ExitStatus == RemoteScripts.ExitUnsupported)
		{
			return MarkChecksumUnavailable();
		}

		if (result.ExitStatus != 0)
		{
			throw RemoteCommandErrors.ToException(result.ExitStatus, result.Error, target);
		}

		return RemoteOutput.ReadSha256(payload.Span) ?? MarkChecksumUnavailable();
	}

	public async ValueTask CopyAsync(string sourcePath, string destinationPath, bool overwrite, CancellationToken cancellationToken = default)
	{
		string source = RemotePath.Normalize(sourcePath);
		string destination = RemotePath.Normalize(destinationPath);
		await RunScriptAsync(RemoteScripts.Copy, RemoteScriptArguments.Copy(source, destination, overwrite), source, destination, "copying", cancellationToken);
	}

	public async ValueTask CompressAsync(string directory, IReadOnlyList<string> names, string archivePath, CancellationToken cancellationToken = default)
	{
		string folder = RemotePath.Normalize(directory);
		string archive = RemotePath.Normalize(archivePath);
		await RunScriptAsync(RemoteScripts.Compress, RemoteScriptArguments.Compress(folder, names, archive), folder, archive, "making an archive", cancellationToken);
	}

	public async ValueTask ExtractAsync(string archivePath, string destinationDirectory, CancellationToken cancellationToken = default)
	{
		string archive = RemotePath.Normalize(archivePath);
		string destination = RemotePath.Normalize(destinationDirectory);
		await RunScriptAsync(RemoteScripts.Extract, RemoteScriptArguments.Extract(archive, destination), archive, destination, "unpacking an archive", cancellationToken);
	}

	/// <summary>The session owns this file system and closes it with <see cref="CloseAsync"/>; disposing a shared instance does nothing.</summary>
	public ValueTask DisposeAsync() => ValueTask.CompletedTask;

	/// <summary>Stops new operations and waits for a running one, so nothing uses the client after the session closes it.</summary>
	public async ValueTask CloseAsync()
	{
		if (Interlocked.Exchange(ref _closed, 1) != 0)
		{
			return;
		}

		await _gate.WaitAsync();
		_gate.Release();
	}

	/// <summary>Turns <c>~</c>, <c>~/x</c> and relative paths into absolute paths under <paramref name="home"/>.</summary>
	internal static string ExpandHome(string path, string home)
	{
		if (path.Length == 0 || path == "~")
		{
			return home;
		}

		if (path.StartsWith("~/", StringComparison.Ordinal))
		{
			return RemotePath.Combine(home, path[2..]);
		}

		return path.StartsWith('/') ? path : RemotePath.Combine(home, path);
	}

	private static RemoteFileEntry ToEntry(string path, SftpFileAttributes attributes, AccountDirectory accounts)
	{
		DateTime modified = attributes.LastWriteTimeUtc;
		return new RemoteFileEntry
		{
			Name = RemotePath.GetName(path),
			Path = path,
			Kind = UnixModes.KindOf(attributes),
			Size = attributes.Size,
			LastModified = modified == DateTime.MinValue ? null : new DateTimeOffset(DateTime.SpecifyKind(modified, DateTimeKind.Utc)),
			Permissions = UnixModes.FromAttributes(attributes),
			Owner = accounts.UserName(attributes.UserId),
			Group = accounts.GroupName(attributes.GroupId),
		};
	}

	private static bool IsRealDirectory(ISftpFile file) => file.IsDirectory && !file.IsSymbolicLink;

	/// <summary>The entry at <paramref name="path"/> itself, not what a link there points at, or null when nothing exists.</summary>
	private static async Task<ISftpFile?> LocateAsync(SftpClient client, string path, CancellationToken cancellationToken)
	{
		if (RemotePath.IsRoot(path))
		{
			return await GetOrNullAsync(client, path, cancellationToken);
		}

		ISftpFile? parent = await GetOrNullAsync(client, RemotePath.GetParent(path), cancellationToken);
		if (parent is not { IsDirectory: true })
		{
			return null;
		}

		string name = RemotePath.GetName(path);
		ISftpFile? resolved = await GetOrNullAsync(client, path, cancellationToken);
		if (resolved is not null && string.Equals(resolved.FullName, RemotePath.Combine(parent.FullName, name), StringComparison.Ordinal))
		{
			return resolved;
		}

		// The resolved path differs (a link) or nothing resolved (possibly a dangling link): read the entry from its parent.
		await foreach (ISftpFile entry in client.ListDirectoryAsync(parent.FullName, cancellationToken))
		{
			if (string.Equals(entry.Name, name, StringComparison.Ordinal))
			{
				return entry;
			}
		}

		return null;
	}

	private static async Task<ISftpFile?> GetOrNullAsync(SftpClient client, string path, CancellationToken cancellationToken)
	{
		try
		{
			return await client.GetAsync(path, cancellationToken);
		}
		catch (SftpPathNotFoundException)
		{
			return null;
		}
		catch (SftpException ex) when (ex.StatusCode == StatusCode.NoSuchFile)
		{
			return null;
		}
	}

	private static async Task<bool> HasChildrenAsync(SftpClient client, string directory, CancellationToken cancellationToken)
	{
		await foreach (ISftpFile entry in client.ListDirectoryAsync(directory, cancellationToken))
		{
			if (entry.Name is not ("." or ".."))
			{
				return true;
			}
		}

		return false;
	}

	private static async Task<bool> TryPosixRenameAsync(SftpClient client, string source, string destination, CancellationToken cancellationToken)
	{
		try
		{
			// posix-rename@openssh.com replaces the destination atomically; SSH.NET only offers it synchronously.
			await Task.Run(() => client.RenameFile(source, destination, isPosix: true), cancellationToken);
			return true;
		}
		catch (NotSupportedException)
		{
			return false;
		}
	}

	private static async Task DeleteExistingAsync(SftpClient client, ISftpFile existing, string path, CancellationToken cancellationToken)
	{
		try
		{
			await existing.DeleteAsync(cancellationToken);
		}
		catch (SftpException ex) when (IsRealDirectory(existing) && ex is not (SftpPathNotFoundException or SftpPermissionDeniedException))
		{
			if (await HasChildrenAsync(client, existing.FullName, cancellationToken))
			{
				throw new RemoteFileSystemException(RemoteFileErrorKind.DirectoryNotEmpty, $"{path} is a directory that is not empty.", path, ex);
			}

			throw;
		}
	}

	private static async Task ResolveLinksOverSftpAsync(SftpClient client, List<RemoteFileEntry> entries, CancellationToken cancellationToken)
	{
		for (int i = 0; i < entries.Count; i++)
		{
			if (entries[i].Kind == RemoteEntryKind.SymbolicLink)
			{
				entries[i] = await WithSftpLinkTargetAsync(client, entries[i], cancellationToken);
			}
		}
	}

	/// <summary>SFTP has no public readlink in SSH.NET, so the target is the server's fully resolved path.</summary>
	private static async Task<RemoteFileEntry> WithSftpLinkTargetAsync(SftpClient client, RemoteFileEntry link, CancellationToken cancellationToken)
	{
		try
		{
			ISftpFile target = await client.GetAsync(link.Path, cancellationToken);
			return link with { LinkTarget = target.FullName, LinkTargetKind = UnixModes.KindOf(target.Attributes) };
		}
		catch (SftpException)
		{
			// A dangling or unreadable target: the link is listed without one.
			return link;
		}
	}

	private static RemoteFileSystemException UnknownAccount(AccountDirectory accounts, string kind, string name, string path) =>
		accounts.IsEmpty
			? new RemoteFileSystemException(RemoteFileErrorKind.NotSupported, $"Account names cannot be read on this server. Use a numeric {kind} id instead of '{name}'.", path)
			: new RemoteFileSystemException(RemoteFileErrorKind.Unknown, $"There is no {kind} named '{name}' on the server.", path);

	private async Task ResolveLinksOverShellAsync(SshClient shell, string directory, List<RemoteFileEntry> entries, CancellationToken cancellationToken)
	{
		try
		{
			int[] links = [.. Enumerable.Range(0, entries.Count).Where(i => entries[i].Kind == RemoteEntryKind.SymbolicLink)];
			foreach (int[] batch in links.Chunk(LinkBatchSize))
			{
				string command = PosixShell.ScriptCommand(RemoteScripts.LinkTargets, [directory, .. batch.Select(i => entries[i].Name)]);
				RemoteCommandResult result = await RemoteCommand.RunAsync(shell, command, null, cancellationToken);
				if (!RemoteOutput.TryGetPayload(result.Output, out ReadOnlyMemory<byte> payload))
				{
					throw new InvalidOperationException("The link target script did not run.");
				}

				Dictionary<string, (string Kind, string Target)> targets = new(StringComparer.Ordinal);
				List<string> fields = RemoteOutput.SplitFields(payload.Span);
				for (int field = 0; field + 3 <= fields.Count; field += 3)
				{
					targets[fields[field]] = (fields[field + 1], fields[field + 2]);
				}

				foreach (int index in batch)
				{
					if (targets.TryGetValue(entries[index].Name, out (string Kind, string Target) link))
					{
						entries[index] = entries[index] with
						{
							LinkTarget = link.Target.Length == 0 ? null : link.Target,
							LinkTargetKind = link.Kind switch
							{
								"d" => RemoteEntryKind.Directory,
								"f" => RemoteEntryKind.File,
								_ => null,
							},
						};
					}
				}
			}
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			// Accounts limited to SFTP cannot run commands; resolve over SFTP from now on.
			_logger.LogDebug("Reading link targets through the shell failed; using SFTP: {Error}", LogSafe.Describe(ex));
			_shellLinksUnavailable = true;
			await RunAsync(directory, async (client, token) =>
			{
				await ResolveLinksOverSftpAsync(client, entries, token);
				return true;
			}, cancellationToken);
		}
	}

	/// <summary>Opens a file for reading at <paramref name="offset"/>, refusing a folder and a file shorter than that.</summary>
	private Task<SftpFileStream> OpenAtAsync(string target, long offset, CancellationToken cancellationToken) =>
		RunAsync(target, async (client, token) =>
		{
			SftpFileAttributes attributes = await client.GetAttributesAsync(target, token);
			if (attributes.IsDirectory)
			{
				throw new RemoteFileSystemException(RemoteFileErrorKind.Unknown, $"{target} is a directory.", target);
			}

			if (attributes.Size < offset)
			{
				throw TransferStreams.ShorterThanCopied(target, offset);
			}

			SftpFileStream stream = await client.OpenAsync(target, FileMode.Open, FileAccess.Read, token);
			if (offset > 0)
			{
				stream.Seek(offset, SeekOrigin.Begin);
			}

			return stream;
		}, cancellationToken);

	/// <summary>
	/// Uploads through a temporary file beside where the upload ends up, which a failed attempt keeps and the next one
	/// continues. False when no temporary file can be used here (the folder takes no new files, or the old file's owner
	/// cannot be kept, which only root may change); the upload then goes the way it always did, without resuming.
	/// </summary>
	private async Task<bool> TryUploadResumableAsync(string target, Stream source, UploadOptions options, UploadResume resume, IProgress<long>? progress, CancellationToken cancellationToken)
	{
		if (resume is { PartialPath: { } partial, IsTemporary: true })
		{
			if (await TryOpenPartialAsync(partial, cancellationToken) is { } continued)
			{
				await ContinueAsync(target, partial, continued, source, options, resume, progress, cancellationToken);
				return true;
			}

			// Someone removed it, or the server was cleaned up meanwhile: there is nothing to continue.
			resume.Clear();
		}

		RemoteFileEntry? existing = await StatAsync(target, cancellationToken);
		if (existing is not null && !options.Overwrite)
		{
			// Found before a byte is sent, rather than after the whole file went into a temporary one.
			throw RemoteFileErrors.AlreadyExists(target);
		}

		if ((existing is not null
				? await TryOpenReplacementAsync(target, options.Permissions, cancellationToken)
				: await TryOpenNewPartialAsync(target, options.Permissions, cancellationToken)) is not { } placement)
		{
			return false;
		}

		resume.Record(placement.Temporary, isTemporary: true);
		await PlaceAsync(placement, source, options, resume, replace: existing is not null || options.Overwrite, progress, cancellationToken);
		return true;
	}

	/// <summary>Carries on with what an earlier attempt left in <paramref name="partial"/>, then moves it into place.</summary>
	private async Task ContinueAsync(
		string target,
		string partial,
		(SftpFileStream Stream, long Length) continued,
		Stream source,
		UploadOptions options,
		UploadResume resume,
		IProgress<long>? progress,
		CancellationToken cancellationToken)
	{
		string destination;
		bool replace;

		// Set only for the two outcomes that make the partial file useless; anything else, a dropped connection among
		// them, leaves it for the next attempt.
		bool abandon = false;
		try
		{
			// Where it goes is looked up again: the old file may have gone, or a link on the way been changed.
			(destination, replace) = await RunAsync(target, async (client, token) =>
				await GetOrNullAsync(client, target, token) is { Attributes.IsRegularFile: true } file
					? (file.FullName, true)
					: (target, options.Overwrite), cancellationToken);

			if (!replace && await StatAsync(target, cancellationToken) is not null)
			{
				abandon = true;
				throw RemoteFileErrors.AlreadyExists(target);
			}

			if (!await TransferStreams.TrySkipAsync(source, continued.Length, cancellationToken))
			{
				abandon = true;
				throw new RemoteFileSystemException(
					RemoteFileErrorKind.Unknown,
					$"The upload to {target} could not carry on: the file being sent is now shorter than what the last attempt wrote. The next try starts over.",
					target);
			}
		}
		catch
		{
			await CloseStreamAsync(continued.Stream, partial, written: false);
			if (abandon)
			{
				await DeleteTemporaryAsync(partial);
				resume.Clear();
			}

			throw;
		}

		resume.NotifyResumed(continued.Length);
		await PlaceAsync((destination, partial, continued.Stream), source, options, resume, replace, progress, cancellationToken);
	}

	/// <summary>Opens what an earlier attempt left for writing after its last byte, or null when it is no longer a regular file.</summary>
	private Task<(SftpFileStream Stream, long Length)?> TryOpenPartialAsync(string partial, CancellationToken cancellationToken) =>
		RunAsync<(SftpFileStream Stream, long Length)?>(partial, async (client, token) =>
		{
			if (await LocateAsync(client, partial, token) is not { Attributes.IsRegularFile: true } file)
			{
				return null;
			}

			// Open and seek rather than append: SFTP writes carry their own offset, and servers differ on what append means.
			SftpFileStream stream = await client.OpenAsync(partial, FileMode.Open, FileAccess.Write, token);
			long length = file.Attributes.Size;
			stream.Seek(length, SeekOrigin.Begin);
			return (stream, length);
		}, cancellationToken);

	/// <summary>
	/// Opens a temporary file beside where a new file will go, with <paramref name="permissions"/> set before any data
	/// arrives. Null when the folder takes no new files that way, and the upload then writes in place.
	/// </summary>
	private Task<(string Destination, string Temporary, SftpFileStream Stream)?> TryOpenNewPartialAsync(string target, UnixFileMode? permissions, CancellationToken cancellationToken) =>
		RunAsync<(string Destination, string Temporary, SftpFileStream Stream)?>(target, async (client, token) =>
		{
			string temporary = TemporaryBeside(target);
			SftpFileStream stream;
			try
			{
				stream = await client.OpenAsync(temporary, FileMode.CreateNew, FileAccess.Write, token);
			}
			catch (SftpException ex)
			{
				_logger.LogDebug("No temporary file could be made for a resumable upload; writing in place: {Error}", LogSafe.Describe(ex));
				return null;
			}

			if (permissions is not { } mode)
			{
				return (target, temporary, stream);
			}

			try
			{
				SftpFileAttributes attributes = await client.GetAttributesAsync(temporary, token);
				UnixModes.ApplyTo(attributes, mode);
				await Task.Run(() => client.SetAttributes(temporary, attributes), token);
				return (target, temporary, stream);
			}
			catch (SftpException ex)
			{
				_logger.LogDebug("The temporary file of a resumable upload cannot take its permissions; writing in place: {Error}", LogSafe.Describe(ex));
				await DiscardTemporaryAsync(client, stream, temporary);
				return null;
			}
		}, cancellationToken);

	private static string TemporaryBeside(string destination) =>
		RemotePath.Combine(RemotePath.GetParent(destination), ".mokaterm-" + RandomNumberGenerator.GetHexString(16, lowercase: true) + ".part");

	private bool CanRunCommands => Volatile.Read(ref _commandsUnavailable) == 0 && _shell() is not null;

	/// <summary>
	/// Runs one of <see cref="RemoteScripts"/> as the logged-in user on the session's SSH connection. A script that never
	/// printed its marker ran nothing at all, which is an account that may only use SFTP, and that does not change while
	/// the session lives.
	/// </summary>
	private async Task RunScriptAsync(string script, string[] arguments, string path, string? destination, string action, CancellationToken cancellationToken)
	{
		ThrowIfClosed();
		if (!CanRunCommands || _shell() is not { } shell)
		{
			throw CommandsUnavailable(action, path);
		}

		RemoteCommandResult result;
		try
		{
			result = await RemoteCommand.RunAsync(shell, PosixShell.ScriptCommand(script, arguments), null, cancellationToken);
		}
		catch (Exception ex) when (RemoteFileErrors.TryMap(ex, path, out RemoteFileSystemException? mapped))
		{
			throw mapped;
		}

		if (!RemoteOutput.TryGetPayload(result.Output, out _))
		{
			if (Interlocked.Exchange(ref _commandsUnavailable, 1) == 0)
			{
				_logger.LogDebug("The account runs no commands; copies and archives are off for this session");
			}

			throw CommandsUnavailable(action, path);
		}

		if (result.ExitStatus != 0)
		{
			throw RemoteCommandErrors.ToException(result.ExitStatus, result.Error, path, destination);
		}
	}

	private static RemoteFileSystemException CommandsUnavailable(string action, string path) =>
		new(RemoteFileErrorKind.NotSupported, $"This connection cannot run commands on the server, which {action} needs.", path);

	private string? MarkChecksumUnavailable()
	{
		if (Interlocked.Exchange(ref _checksumUnavailable, 1) == 0)
		{
			_logger.LogDebug("The server cannot compute SHA-256 checksums for this account; transfers stay unverified");
		}

		return null;
	}

	private async Task CopyToRemoteAsync(Stream source, SftpFileStream stream, string target, IProgress<long>? progress, CancellationToken cancellationToken)
	{
		byte[] buffer = ArrayPool<byte>.Shared.Rent(TransferBufferSize);
		try
		{
			long total = 0;
			int read;
			while ((read = await source.ReadAsync(buffer.AsMemory(0, TransferBufferSize), cancellationToken)) > 0)
			{
				ReadOnlyMemory<byte> chunk = buffer.AsMemory(0, read);
				await RunAsync(target, async (_, token) =>
				{
					await stream.WriteAsync(chunk, token);
					return true;
				}, cancellationToken);

				total += read;
				progress?.Report(total);
			}

			await RunAsync(target, async (_, token) =>
			{
				await stream.FlushAsync(token);
				return true;
			}, cancellationToken);
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(buffer);
		}
	}

	/// <summary>
	/// Opens a temporary file beside the regular file at <paramref name="target"/>, or beside the file a link on the way
	/// points at, with that file's owner and group and its permissions (or <paramref name="permissions"/>) set before any
	/// data arrives. Null when there is no file to replace, or when the folder takes no new files or the owner cannot be
	/// kept, which only root may change: the upload then writes in place as it always did.
	/// </summary>
	private async Task<(string Destination, string Temporary, SftpFileStream Stream)?> TryOpenReplacementAsync(string target, UnixFileMode? permissions, CancellationToken cancellationToken) =>
		await RunAsync<(string Destination, string Temporary, SftpFileStream Stream)?>(target, async (client, token) =>
		{
			// The resolved path is the file an in-place write would have changed, links included.
			if (await GetOrNullAsync(client, target, token) is not { Attributes.IsRegularFile: true } file)
			{
				return null;
			}

			string destination = file.FullName;
			string temporary = TemporaryBeside(destination);
			SftpFileStream stream;
			try
			{
				stream = await client.OpenAsync(temporary, FileMode.CreateNew, FileAccess.Write, token);
			}
			catch (SftpException ex)
			{
				_logger.LogDebug("No temporary file could be made beside the file an upload replaces; writing in place: {Error}", LogSafe.Describe(ex));
				return null;
			}

			bool keep = false;
			try
			{
				SftpFileAttributes attributes = await client.GetAttributesAsync(temporary, token);
				attributes.UserId = file.Attributes.UserId;
				attributes.GroupId = file.Attributes.GroupId;

				// Only the permission bits: an in-place write clears setuid and setgid, so new content must not get them back.
				UnixModes.ApplyTo(attributes, permissions ?? (UnixModes.FromAttributes(file.Attributes) & PermissionBits));
				await Task.Run(() => client.SetAttributes(temporary, attributes), token);
				keep = true;
				return (destination, temporary, stream);
			}
			catch (SftpException ex)
			{
				_logger.LogDebug("The temporary file of an upload cannot take the old file's owner or permissions; writing in place: {Error}", LogSafe.Describe(ex));
				return null;
			}
			finally
			{
				if (!keep)
				{
					await DiscardTemporaryAsync(client, stream, temporary);
				}
			}
		}, cancellationToken);

	/// <summary>
	/// Uploads into the temporary file and moves it to <c>Destination</c>. Whatever was there stays as it was until then.
	/// <paramref name="replace"/> false refuses a destination that appeared meanwhile. A resumable upload keeps the
	/// temporary file when the copy fails, for the next attempt to continue.
	/// </summary>
	private async Task PlaceAsync(
		(string Destination, string Temporary, SftpFileStream Stream) placement,
		Stream source,
		UploadOptions options,
		UploadResume? resume,
		bool replace,
		IProgress<long>? progress,
		CancellationToken cancellationToken)
	{
		(string destination, string temporary, SftpFileStream stream) = placement;
		bool placed = false;
		bool oldFileRemoved = false;
		bool keepTemporary = resume is not null;
		try
		{
			bool written = false;
			try
			{
				await CopyToRemoteAsync(source, stream, temporary, progress, cancellationToken);
				written = true;
			}
			finally
			{
				await CloseStreamAsync(stream, temporary, written);
			}

			if (options.LastModified is { } lastModified)
			{
				await SetAttributesAsync(temporary, attributes => attributes.LastWriteTimeUtc = lastModified.UtcDateTime, cancellationToken);
			}

			// Not cancellable from here: once the old file starts to go, the new one has to arrive.
			await RunAsync(destination, async (client, _) =>
			{
				if (replace && await TryPosixRenameAsync(client, temporary, destination, CancellationToken.None))
				{
					placed = true;
					return true;
				}

				if (replace && await LocateAsync(client, destination, CancellationToken.None) is not null)
				{
					// Plain SFTP rename refuses an existing target, so the old file goes first; the new one is complete by now.
					await client.DeleteFileAsync(destination, CancellationToken.None);
					oldFileRemoved = true;
				}

				try
				{
					await client.RenameFileAsync(temporary, destination, CancellationToken.None);
				}
				catch (SftpException) when (!replace)
				{
					if (await LocateAsync(client, destination, CancellationToken.None) is null)
					{
						throw;
					}

					// Told not to overwrite, and something took the name while the upload ran: this copy has nowhere to go.
					keepTemporary = false;
					throw RemoteFileErrors.AlreadyExists(destination);
				}

				placed = true;
				return true;
			}, CancellationToken.None);
		}
		catch (RemoteFileSystemException ex) when (oldFileRemoved && !placed)
		{
			throw new RemoteFileSystemException(
				ex.Kind,
				$"The upload finished, but it could not take the place of {destination}, which was already removed: {ex.Message} The uploaded file is kept as {temporary}.",
				destination,
				ex);
		}
		finally
		{
			// After the old file was removed the temporary file is the only copy, so it stays.
			if (!placed && !oldFileRemoved && !keepTemporary)
			{
				await DeleteTemporaryAsync(temporary);
				resume?.Clear();
			}

			if (placed)
			{
				resume?.Clear();
			}
		}
	}

	private async Task CloseStreamAsync(SftpFileStream stream, string path, bool written)
	{
		try
		{
			await RunAsync(path, async (_, _) =>
			{
				await stream.DisposeAsync();
				return true;
			}, CancellationToken.None);
		}
		catch (RemoteFileSystemException) when (!written)
		{
			// The upload already failed; that failure is the one to report.
		}
	}

	private async Task DeleteTemporaryAsync(string temporary)
	{
		try
		{
			await RunAsync(temporary, async (client, _) =>
			{
				await client.DeleteFileAsync(temporary, CancellationToken.None);
				return true;
			}, CancellationToken.None);
		}
		catch (RemoteFileSystemException ex)
		{
			_logger.LogDebug("Could not remove the temporary file of an upload: {Error}", LogSafe.Describe(ex));
		}
	}

	/// <summary>Closes and removes a temporary file from inside an operation that already holds the lock.</summary>
	private async Task DiscardTemporaryAsync(SftpClient client, SftpFileStream stream, string temporary)
	{
		try
		{
			await stream.DisposeAsync();
			await client.DeleteFileAsync(temporary, CancellationToken.None);
		}
		catch (Exception ex) when (RemoteFileErrors.TryMap(ex, temporary, out _))
		{
			_logger.LogDebug("Could not remove the temporary file of an upload: {Error}", LogSafe.Describe(ex));
		}
	}

	private async Task CloseUploadAsync(SftpFileStream stream, string target, bool written)
	{
		await CloseStreamAsync(stream, target, written);
		if (written)
		{
			return;
		}

		try
		{
			await RunAsync(target, async (client, token) =>
			{
				if (await LocateAsync(client, target, token) is { } partial)
				{
					await partial.DeleteAsync(token);
				}

				return true;
			}, CancellationToken.None);
		}
		catch (RemoteFileSystemException ex)
		{
			_logger.LogDebug("Could not remove a partial upload: {Error}", LogSafe.Describe(ex));
		}
	}

	private async Task SetAttributesAsync(string target, Action<SftpFileAttributes> change, CancellationToken cancellationToken) =>
		await RunAsync(target, async (client, token) =>
		{
			SftpFileAttributes attributes = await client.GetAttributesAsync(target, token);
			change(attributes);

			// SSH.NET only offers setstat synchronously; it sends just the attributes that changed.
			await Task.Run(() => client.SetAttributes(target, attributes), token);
			return true;
		}, cancellationToken);

	/// <summary>
	/// Applies <paramref name="change"/> to the entry and, when recursive, to everything below it without following links.
	/// The change returns false for an entry it leaves alone.
	/// </summary>
	private async Task ApplyAttributesAsync(ISftpFile entry, bool recursive, Func<ISftpFile, bool> change, CancellationToken cancellationToken)
	{
		await RunAsync(entry.FullName, async (_, token) =>
		{
			if (change(entry))
			{
				await Task.Run(entry.UpdateStatus, token);
			}

			return true;
		}, cancellationToken);

		if (!recursive || !IsRealDirectory(entry))
		{
			return;
		}

		foreach (ISftpFile child in await ListChildrenAsync(entry.FullName, cancellationToken))
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!child.IsSymbolicLink)
			{
				await ApplyAttributesAsync(child, recursive: true, change, cancellationToken);
			}
		}
	}

	private async Task DeleteTreeAsync(ISftpFile directory, CancellationToken cancellationToken)
	{
		foreach (ISftpFile child in await ListChildrenAsync(directory.FullName, cancellationToken))
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (IsRealDirectory(child))
			{
				await DeleteTreeAsync(child, cancellationToken);
			}
			else
			{
				await RunAsync(child.FullName, async (_, token) =>
				{
					await child.DeleteAsync(token);
					return true;
				}, cancellationToken);
			}
		}

		await RunAsync(directory.FullName, async (_, token) =>
		{
			await directory.DeleteAsync(token);
			return true;
		}, cancellationToken);
	}

	private Task<List<ISftpFile>> ListChildrenAsync(string directory, CancellationToken cancellationToken) =>
		RunAsync(directory, async (client, token) =>
		{
			List<ISftpFile> children = [];
			await foreach (ISftpFile child in client.ListDirectoryAsync(directory, token))
			{
				if (child.Name is not ("." or ".."))
				{
					children.Add(child);
				}
			}

			return children;
		}, cancellationToken);

	private async Task<T> RunAsync<T>(string? path, Func<SftpClient, CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
	{
		ThrowIfClosed();
		await _gate.WaitAsync(cancellationToken);
		try
		{
			ThrowIfClosed();
			return await operation(_client, cancellationToken);
		}
		catch (Exception ex) when (RemoteFileErrors.TryMap(ex, path, out RemoteFileSystemException? mapped))
		{
			throw mapped;
		}
		finally
		{
			_gate.Release();
		}
	}

	private void ThrowIfClosed()
	{
		if (Volatile.Read(ref _closed) != 0)
		{
			throw new RemoteFileSystemException(RemoteFileErrorKind.ConnectionLost, "The session is closed.");
		}
	}
}
