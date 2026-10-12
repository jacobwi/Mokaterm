using System.Buffers;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Modules.Ssh.FileSystem;
using Mokaterm.Modules.Ssh.Shell;

namespace Mokaterm.Modules.Ssh.Elevation;

/// <summary>
/// The same files as root: every operation is a small POSIX script run through sudo on an exec channel. Uploads first go
/// to a private staging file as the logged-in user over SFTP, then move into place as root.
/// </summary>
internal sealed class SudoFileSystem : ScriptFileSystem
{
	private const int TransferBufferSize = 128 * 1024;

	private readonly SudoRunner _runner;
	private readonly Func<CancellationToken, ValueTask<SftpFileSystem>> _userFileSystem;
	private readonly RemoteAccountNames _accountNames;
	private readonly string _stagingDirectory;
	private readonly ILogger _logger;
	private readonly Action<SudoFileSystem> _onDisposed;
	private int _disposed;
	private int _checksumUnavailable;

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

	public override RemoteFileSystemFeatures Features =>
		RemoteFileSystemFeatures.Permissions | RemoteFileSystemFeatures.Ownership | RemoteFileSystemFeatures.SymbolicLinks
		| RemoteFileSystemFeatures.Elevation | RemoteFileSystemFeatures.Accounts
		| RemoteFileSystemFeatures.Copy | RemoteFileSystemFeatures.Archives;

	public override string UserName => "root";

	public override bool IsElevated => true;

	public override async ValueTask<string> GetHomeDirectoryAsync(CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();
		SftpFileSystem user = await _userFileSystem(cancellationToken);
		return await user.GetHomeDirectoryAsync(cancellationToken);
	}

	public override async ValueTask<string> ResolvePathAsync(string path, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(path);
		ThrowIfDisposed();

		// "~" keeps meaning the logged-in user's home, so a path typed in the browser leads to the same place as root.
		SftpFileSystem user = await _userFileSystem(cancellationToken);
		return await ResolveAbsoluteAsync(Absolute(SftpFileSystem.ExpandHome(path, user.HomeDirectory)), cancellationToken);
	}

	public override async ValueTask<RemoteAccounts> ListAccountsAsync(CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();
		return (await _accountNames.GetAsync(cancellationToken)).Accounts;
	}

	public override async ValueTask<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default)
	{
		string target = Absolute(path);
		RemoteFileEntry entry = await StatAsync(target, cancellationToken)
			?? throw new RemoteFileSystemException(RemoteFileErrorKind.NotFound, $"{target} does not exist.", target);

		if (entry.IsDirectoryLike)
		{
			throw new RemoteFileSystemException(RemoteFileErrorKind.Unknown, $"{target} is a directory.", target);
		}

		// The token only covers opening: SSH.NET would cancel the whole command with it.
		return await StartReadAsync(target, RemoteScripts.Read, [target], CancellationToken.None);
	}

	public override async ValueTask UploadAsync(string path, Stream source, UploadOptions options, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(options);
		string target = Absolute(path);
		SftpFileSystem user = await _userFileSystem(cancellationToken);
		await using StagedUpload staged = StagedUpload.In(user, _stagingDirectory, _logger);
		await staged.WriteAsync(source, progress, target, cancellationToken);

		string mode = options.Permissions is { } permissions ? UnixFileModeFormat.ToOctal(permissions) : "";
		string stamp = options.LastModified is { } lastModified
			? lastModified.UtcDateTime.ToString("yyyyMMddHHmm.ss", CultureInfo.InvariantCulture)
			: "";

		await RunScriptAsync(RemoteScripts.PlaceUpload, [staged.Path, target, Flag(options.Overwrite), mode, stamp], target, cancellationToken);
	}

	public override ValueTask DownloadAsync(string path, Stream destination, IProgress<long>? progress = null, CancellationToken cancellationToken = default) =>
		DownloadAsync(path, destination, 0, progress, cancellationToken);

	public override async ValueTask DownloadAsync(string path, Stream destination, long offset, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(destination);
		ArgumentOutOfRangeException.ThrowIfNegative(offset);
		string target = Absolute(path);
		if (offset > 0 && await StatAsync(target, cancellationToken) is { Kind: RemoteEntryKind.File } entry && entry.Size < offset)
		{
			throw TransferStreams.ShorterThanCopied(target, offset);
		}

		// tail starts at the byte it is given counting from one, so the server skips the part already here.
		ElevatedReadStream source = offset == 0
			? await StartReadAsync(target, RemoteScripts.Read, [target], cancellationToken)
			: await StartReadAsync(target, RemoteScripts.ReadFrom, [target, (offset + 1).ToString(CultureInfo.InvariantCulture)], cancellationToken);
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

	public override ChecksumAlgorithm? ServerChecksum => Volatile.Read(ref _checksumUnavailable) == 0 ? ChecksumAlgorithm.Sha256 : null;

	public override async ValueTask<string?> ComputeChecksumAsync(string path, CancellationToken cancellationToken = default)
	{
		string target = Absolute(path);
		if (Volatile.Read(ref _checksumUnavailable) != 0)
		{
			return null;
		}

		ReadOnlyMemory<byte> payload;
		try
		{
			payload = await RunScriptAsync(RemoteScripts.Checksum, [target], target, cancellationToken);
		}
		catch (RemoteFileSystemException ex) when (ex.Kind == RemoteFileErrorKind.NotSupported)
		{
			// No SHA-256 tool on this server, which will not change while the session lives.
			Volatile.Write(ref _checksumUnavailable, 1);
			return null;
		}

		if (RemoteOutput.ReadSha256(payload.Span) is { } checksum)
		{
			return checksum;
		}

		Volatile.Write(ref _checksumUnavailable, 1);
		_logger.LogDebug("The server's checksum tool printed something other than a SHA-256; root transfers stay unverified");
		return null;
	}

	public override ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) == 0)
		{
			_runner.Dispose();
			_onDisposed(this);
		}

		return ValueTask.CompletedTask;
	}

	protected override async Task<ReadOnlyMemory<byte>> RunScriptAsync(
		string script,
		IReadOnlyList<string> arguments,
		string path,
		CancellationToken cancellationToken,
		string? destination = null,
		bool allowPartialListing = false)
	{
		ThrowIfDisposed();
		RemoteCommandResult result;
		try
		{
			result = await _runner.RunAsync(script, arguments, RemoteScripts.RunsLong(script), cancellationToken);
		}
		catch (Exception ex) when (RemoteFileErrors.TryMap(ex, path, out RemoteFileSystemException? mapped))
		{
			throw mapped;
		}

		return SudoRunner.EnsureSucceeded(result, _runner.Account, path, destination, allowPartialListing);
	}

	protected override void ThrowIfDisposed()
	{
		if (Volatile.Read(ref _disposed) != 0)
		{
			throw new RemoteFileSystemException(RemoteFileErrorKind.ElevationFailed, "The root view was closed.");
		}
	}

	private async Task<ElevatedReadStream> StartReadAsync(string target, string script, string[] arguments, CancellationToken cancellationToken)
	{
		ThrowIfDisposed();
		try
		{
			return await _runner.StartReadAsync(script, arguments, target, cancellationToken);
		}
		catch (Exception ex) when (RemoteFileErrors.TryMap(ex, target, out RemoteFileSystemException? mapped))
		{
			throw mapped;
		}
	}
}
