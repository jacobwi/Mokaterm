using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Diagnostics;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Modules.Ssh.FileSystem;
using Mokaterm.Modules.Ssh.Shell;

namespace Mokaterm.Modules.Ssh.Elevation;

/// <summary>
/// The same files as root: every operation is a small POSIX script run through sudo on an exec channel. Uploads first go
/// to a private staging file as the logged-in user over SFTP, then move into place as root.
/// </summary>
internal sealed class SudoFileSystem : IRemoteFileSystem
{
	private const int TransferBufferSize = 128 * 1024;

	private readonly SudoRunner _runner;
	private readonly Func<CancellationToken, ValueTask<SftpFileSystem>> _userFileSystem;
	private readonly RemoteAccountNames _accountNames;
	private readonly string _stagingDirectory;
	private readonly ILogger _logger;
	private readonly Action<SudoFileSystem> _onDisposed;
	private int _disposed;

	/// <param name="userFileSystem">Opens the logged-in user's SFTP file system, for staging uploads and resolving <c>~</c>.</param>
	/// <param name="accountNames">The session's account names; passwd and group read the same for root as for the user.</param>
	/// <param name="onDisposed">Lets the session stop tracking this file system.</param>
	public SudoFileSystem(
		SudoRunner runner,
		Func<CancellationToken, ValueTask<SftpFileSystem>> userFileSystem,
		RemoteAccountNames accountNames,
		string stagingDirectory,
		ILogger logger,
		Action<SudoFileSystem> onDisposed)
	{
		_runner = runner;
		_userFileSystem = userFileSystem;
		_accountNames = accountNames;
		_stagingDirectory = stagingDirectory;
		_logger = logger;
		_onDisposed = onDisposed;
	}

	public RemoteFileSystemFeatures Features =>
		RemoteFileSystemFeatures.Permissions | RemoteFileSystemFeatures.Ownership | RemoteFileSystemFeatures.SymbolicLinks
		| RemoteFileSystemFeatures.Elevation | RemoteFileSystemFeatures.Accounts;

	public string UserName => "root";

	public bool IsElevated => true;

	public async ValueTask<string> GetHomeDirectoryAsync(CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();
		SftpFileSystem user = await _userFileSystem(cancellationToken);
		return await user.GetHomeDirectoryAsync(cancellationToken);
	}

	public async ValueTask<string> ResolvePathAsync(string path, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(path);
		ThrowIfDisposed();

		// "~" keeps meaning the logged-in user's home, so a path typed in the browser leads to the same place as root.
		SftpFileSystem user = await _userFileSystem(cancellationToken);
		string absolute = RemotePath.Normalize(SftpFileSystem.ExpandHome(path, user.HomeDirectory));
		ReadOnlyMemory<byte> payload = await RunAsync(RemoteScripts.Resolve, [absolute], absolute, cancellationToken);
		string resolved = RemoteOutput.ReadLine(payload.Span);
		return resolved.StartsWith('/') ? resolved : absolute;
	}

	public async ValueTask<IReadOnlyList<RemoteFileEntry>> ListAsync(string path, CancellationToken cancellationToken = default)
	{
		string directory = Absolute(path);
		ReadOnlyMemory<byte> payload = await RunAsync(RemoteScripts.List, [directory, "list"], directory, cancellationToken, allowPartialListing: true);
		return RemoteListingParser.Parse(payload.Span, directory, isStat: false);
	}

	public async ValueTask<RemoteFileEntry?> StatAsync(string path, CancellationToken cancellationToken = default)
	{
		string target = Absolute(path);
		try
		{
			ReadOnlyMemory<byte> payload = await RunAsync(RemoteScripts.List, [target, "stat"], target, cancellationToken);
			List<RemoteFileEntry> entries = RemoteListingParser.Parse(payload.Span, target, isStat: true);
			return entries.Count > 0 ? entries[0] : null;
		}
		catch (RemoteFileSystemException ex) when (ex.Kind == RemoteFileErrorKind.NotFound)
		{
			return null;
		}
	}

	public async ValueTask CreateDirectoryAsync(string path, CancellationToken cancellationToken = default)
	{
		string target = Absolute(path);
		await RunAsync(RemoteScripts.CreateDirectory, [target], target, cancellationToken);
	}

	public async ValueTask DeleteAsync(string path, bool recursive, CancellationToken cancellationToken = default)
	{
		string target = Absolute(path);
		if (RemotePath.IsRoot(target))
		{
			throw new RemoteFileSystemException(RemoteFileErrorKind.PermissionDenied, "The root directory cannot be deleted.", target);
		}

		await RunAsync(RemoteScripts.Delete, [target, Flag(recursive)], target, cancellationToken);
	}

	public async ValueTask RenameAsync(string sourcePath, string destinationPath, bool overwrite, CancellationToken cancellationToken = default)
	{
		string source = Absolute(sourcePath);
		string destination = Absolute(destinationPath);
		if (string.Equals(source, destination, StringComparison.Ordinal))
		{
			return;
		}

		await RunAsync(RemoteScripts.Rename, [source, destination, Flag(overwrite)], source, cancellationToken, destination);
	}

	public ValueTask SetPermissionsAsync(string path, UnixFileMode permissions, bool recursive, CancellationToken cancellationToken = default) =>
		ChangePermissionsAsync(path, new PermissionChange { Mode = permissions, Recursive = recursive }, cancellationToken);

	public async ValueTask ChangePermissionsAsync(string path, PermissionChange change, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(change);
		string target = Absolute(path);
		if (ChmodArguments.Mode(change) is not { } mode)
		{
			return;
		}

		await RunAsync(RemoteScripts.ChangeMode, [target, mode, Flag(change.Recursive), ChmodArguments.Targets(change.Targets)], target, cancellationToken);
	}

	public async ValueTask SetOwnerAsync(string path, string owner, string? group, bool recursive, CancellationToken cancellationToken = default)
	{
		string target = Absolute(path);
		string owners = group is null ? AccountName(owner, target) : $"{AccountName(owner, target)}:{AccountName(group, target)}";
		await RunAsync(RemoteScripts.ChangeOwner, [target, owners, Flag(recursive)], target, cancellationToken);
	}

	public async ValueTask SetGroupAsync(string path, string group, bool recursive, CancellationToken cancellationToken = default)
	{
		string target = Absolute(path);
		await RunAsync(RemoteScripts.ChangeGroup, [target, AccountName(group, target), Flag(recursive)], target, cancellationToken);
	}

	public async ValueTask<RemoteAccounts> ListAccountsAsync(CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();
		return (await _accountNames.GetAsync(cancellationToken)).Accounts;
	}

	public async ValueTask<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default)
	{
		string target = Absolute(path);
		RemoteFileEntry entry = await StatAsync(target, cancellationToken)
			?? throw new RemoteFileSystemException(RemoteFileErrorKind.NotFound, $"{target} does not exist.", target);

		if (entry.IsDirectoryLike)
		{
			throw new RemoteFileSystemException(RemoteFileErrorKind.Unknown, $"{target} is a directory.", target);
		}

		// The token only covers opening: SSH.NET would cancel the whole command with it.
		return await StartReadAsync(target, CancellationToken.None);
	}

	public async ValueTask UploadAsync(string path, Stream source, UploadOptions options, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(options);
		string target = Absolute(path);
		SftpFileSystem user = await _userFileSystem(cancellationToken);
		string staging = RemotePath.Combine(_stagingDirectory, ".mokaterm-upload-" + RandomNumberGenerator.GetHexString(32, lowercase: true));
		try
		{
			try
			{
				UploadOptions stagingOptions = new() { Permissions = UnixFileMode.UserRead | UnixFileMode.UserWrite };
				await user.UploadAsync(staging, source, stagingOptions, progress, cancellationToken);
			}
			catch (RemoteFileSystemException ex) when (ex.Kind is not RemoteFileErrorKind.ConnectionLost)
			{
				throw new RemoteFileSystemException(ex.Kind, $"The upload could not be staged in {_stagingDirectory}: {ex.Message}", target, ex);
			}

			string mode = options.Permissions is { } permissions ? UnixFileModeFormat.ToOctal(permissions) : "";
			string stamp = options.LastModified is { } lastModified
				? lastModified.UtcDateTime.ToString("yyyyMMddHHmm.ss", CultureInfo.InvariantCulture)
				: "";

			await RunAsync(RemoteScripts.PlaceUpload, [staging, target, Flag(options.Overwrite), mode, stamp], target, cancellationToken);
		}
		finally
		{
			await RemoveStagingFileAsync(user, staging);
		}
	}

	public async ValueTask DownloadAsync(string path, Stream destination, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(destination);
		string target = Absolute(path);
		ElevatedReadStream source = await StartReadAsync(target, cancellationToken);
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

	public ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) == 0)
		{
			_runner.Dispose();
			_onDisposed(this);
		}

		return ValueTask.CompletedTask;
	}

	private static string Flag(bool value) => value ? "1" : "";

	private static string AccountName(string name, string path) => RemoteAccount.IsValidName(name)
		? name
		: throw new RemoteFileSystemException(RemoteFileErrorKind.Unknown, $"'{name}' is not a valid user or group name.", path);

	private string Absolute(string path)
	{
		ArgumentNullException.ThrowIfNull(path);
		ThrowIfDisposed();
		string normalized = RemotePath.Normalize(path);
		return normalized.Contains('\0', StringComparison.Ordinal)
			? throw new RemoteFileSystemException(RemoteFileErrorKind.NotFound, "Paths cannot contain NUL characters.", path)
			: normalized;
	}

	private async Task<ReadOnlyMemory<byte>> RunAsync(string script, string[] arguments, string path, CancellationToken cancellationToken, string? destination = null, bool allowPartialListing = false)
	{
		ThrowIfDisposed();
		RemoteCommandResult result;
		try
		{
			result = await _runner.RunAsync(script, arguments, cancellationToken);
		}
		catch (Exception ex) when (RemoteFileErrors.TryMap(ex, path, out RemoteFileSystemException? mapped))
		{
			throw mapped;
		}

		return SudoRunner.EnsureSucceeded(result, _runner.Account, path, destination, allowPartialListing);
	}

	private async Task<ElevatedReadStream> StartReadAsync(string target, CancellationToken cancellationToken)
	{
		ThrowIfDisposed();
		try
		{
			return await _runner.StartReadAsync(RemoteScripts.Read, [target], target, cancellationToken);
		}
		catch (Exception ex) when (RemoteFileErrors.TryMap(ex, target, out RemoteFileSystemException? mapped))
		{
			throw mapped;
		}
	}

	private async Task RemoveStagingFileAsync(SftpFileSystem user, string staging)
	{
		try
		{
			await user.DeleteAsync(staging, recursive: false, CancellationToken.None);
		}
		catch (RemoteFileSystemException ex) when (ex.Kind == RemoteFileErrorKind.NotFound)
		{
			// Moved into place, or already removed by the script.
		}
		catch (RemoteFileSystemException ex)
		{
			_logger.LogWarning("A staging file for a root upload could not be removed: {Error}", LogSafe.Describe(ex));
		}
	}

	private void ThrowIfDisposed()
	{
		if (Volatile.Read(ref _disposed) != 0)
		{
			throw new RemoteFileSystemException(RemoteFileErrorKind.ElevationFailed, "The root view was closed.");
		}
	}
}
