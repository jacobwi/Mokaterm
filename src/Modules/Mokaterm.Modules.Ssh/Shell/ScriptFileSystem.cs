using System.Buffers;
using System.Globalization;
using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.Modules.Ssh.Shell;

/// <summary>
/// A file system where every operation is one of <see cref="RemoteScripts"/>, run with its paths as positional arguments.
/// The root view runs them through sudo and a container's files through docker exec; which script an operation picks,
/// what it passes and how the output reads back is the same for both and lives here. An operation whose feature the
/// file system does not declare fails as not supported instead of running.
/// </summary>
/// <remarks>
/// Reads are pieces of <see cref="RemoteScripts.ReadRange"/> (<see cref="PieceReadStream"/>) on both, so what a file
/// costs in memory is one piece, however slowly its reader takes it.
/// </remarks>
internal abstract class ScriptFileSystem : IRemoteFileSystem
{
	/// <summary>
	/// What a piece's output may hold beyond the bytes asked for: the marker, the file's version, and anything the
	/// login's shell printed first. A write prints no more than this either.
	/// </summary>
	protected const int PieceSlackBytes = 64 * 1024;

	private const int TransferBufferSize = 128 * 1024;

	public abstract RemoteFileSystemFeatures Features { get; }

	public abstract string UserName { get; }

	public abstract bool IsElevated { get; }

	public virtual ChecksumAlgorithm? ServerChecksum => null;

	public abstract ValueTask<string> GetHomeDirectoryAsync(CancellationToken cancellationToken = default);

	public abstract ValueTask<string> ResolvePathAsync(string path, CancellationToken cancellationToken = default);

	/// <summary>
	/// The file a piece at a time. The first piece is read here, so a file that is missing, a folder or unreadable fails
	/// at the open; the token covers only that piece, each later read brings its own.
	/// </summary>
	public async ValueTask<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default)
	{
		string target = Absolute(path);
		return await OpenPiecesAsync(target, 0, cancellationToken);
	}

	public abstract ValueTask UploadAsync(string path, Stream source, UploadOptions options, IProgress<long>? progress = null, CancellationToken cancellationToken = default);

	public ValueTask DownloadAsync(string path, Stream destination, IProgress<long>? progress = null, CancellationToken cancellationToken = default) =>
		DownloadAsync(path, destination, 0, progress, cancellationToken);

	/// <summary>
	/// Copies the file from <paramref name="offset"/> on, a piece at a time: a download that continues starts its first
	/// piece there, so the server never sends the part already copied.
	/// </summary>
	public async ValueTask DownloadAsync(string path, Stream destination, long offset, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(destination);
		ArgumentOutOfRangeException.ThrowIfNegative(offset);
		string target = Absolute(path);
		if (offset > 0 && await StatAsync(target, cancellationToken) is { Kind: RemoteEntryKind.File } entry && entry.Size < offset)
		{
			throw TransferStreams.ShorterThanCopied(target, offset);
		}

		await using PieceReadStream source = await OpenPiecesAsync(target, offset, cancellationToken);
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

	public virtual ValueTask<string?> ComputeChecksumAsync(string path, CancellationToken cancellationToken = default) =>
		ValueTask.FromResult<string?>(null);

	public virtual ValueTask<RemoteAccounts> ListAccountsAsync(CancellationToken cancellationToken = default) =>
		ValueTask.FromResult(RemoteAccounts.Empty);

	public abstract ValueTask DisposeAsync();

	public async ValueTask<IReadOnlyList<RemoteFileEntry>> ListAsync(string path, CancellationToken cancellationToken = default)
	{
		string directory = Absolute(path);
		ReadOnlyMemory<byte> payload = await RunScriptAsync(RemoteScripts.List, [directory, "list"], directory, cancellationToken, allowPartialListing: true);
		return RemoteListingParser.Parse(payload.Span, directory, isStat: false);
	}

	public async ValueTask<RemoteFileEntry?> StatAsync(string path, CancellationToken cancellationToken = default)
	{
		string target = Absolute(path);
		try
		{
			ReadOnlyMemory<byte> payload = await RunScriptAsync(RemoteScripts.List, [target, "stat"], target, cancellationToken);
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
		await RunScriptAsync(RemoteScripts.CreateDirectory, [target], target, cancellationToken);
	}

	public async ValueTask DeleteAsync(string path, bool recursive, CancellationToken cancellationToken = default)
	{
		string target = Absolute(path);
		if (RemotePath.IsRoot(target))
		{
			throw new RemoteFileSystemException(RemoteFileErrorKind.PermissionDenied, "The root directory cannot be deleted.", target);
		}

		await RunScriptAsync(RemoteScripts.Delete, [target, Flag(recursive)], target, cancellationToken);
	}

	public async ValueTask RenameAsync(string sourcePath, string destinationPath, bool overwrite, CancellationToken cancellationToken = default)
	{
		string source = Absolute(sourcePath);
		string destination = Absolute(destinationPath);
		if (string.Equals(source, destination, StringComparison.Ordinal))
		{
			return;
		}

		await RunScriptAsync(RemoteScripts.Rename, [source, destination, Flag(overwrite)], source, cancellationToken, destination);
	}

	public async ValueTask CopyAsync(string sourcePath, string destinationPath, bool overwrite, CancellationToken cancellationToken = default)
	{
		string source = Absolute(sourcePath);
		string destination = Absolute(destinationPath);
		Require(RemoteFileSystemFeatures.Copy, "copy on the server", source);
		await RunScriptAsync(RemoteScripts.Copy, RemoteScriptArguments.Copy(source, destination, overwrite), source, cancellationToken, destination);
	}

	public async ValueTask CompressAsync(string directory, IReadOnlyList<string> names, string archivePath, CancellationToken cancellationToken = default)
	{
		string folder = Absolute(directory);
		string archive = Absolute(archivePath);
		Require(RemoteFileSystemFeatures.Archives, "make archives on the server", archive);
		await RunScriptAsync(RemoteScripts.Compress, RemoteScriptArguments.Compress(folder, names, archive), folder, cancellationToken, archive);
	}

	public async ValueTask ExtractAsync(string archivePath, string destinationDirectory, CancellationToken cancellationToken = default)
	{
		string archive = Absolute(archivePath);
		string destination = Absolute(destinationDirectory);
		Require(RemoteFileSystemFeatures.Archives, "unpack archives on the server", archive);
		await RunScriptAsync(RemoteScripts.Extract, RemoteScriptArguments.Extract(archive, destination), archive, cancellationToken, destination);
	}

	public ValueTask SetPermissionsAsync(string path, UnixFileMode permissions, bool recursive, CancellationToken cancellationToken = default) =>
		ChangePermissionsAsync(path, new PermissionChange { Mode = permissions, Recursive = recursive }, cancellationToken);

	public async ValueTask ChangePermissionsAsync(string path, PermissionChange change, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(change);
		string target = Absolute(path);
		Require(RemoteFileSystemFeatures.Permissions, "change permissions", target);
		if (ChmodArguments.Mode(change) is not { } mode)
		{
			return;
		}

		await RunScriptAsync(RemoteScripts.ChangeMode, [target, mode, Flag(change.Recursive), ChmodArguments.Targets(change.Targets)], target, cancellationToken);
	}

	public async ValueTask SetOwnerAsync(string path, string owner, string? group, bool recursive, CancellationToken cancellationToken = default)
	{
		string target = Absolute(path);
		Require(RemoteFileSystemFeatures.Ownership, "change owners", target);
		string owners = group is null ? AccountName(owner, target) : $"{AccountName(owner, target)}:{AccountName(group, target)}";
		await RunScriptAsync(RemoteScripts.ChangeOwner, [target, owners, Flag(recursive)], target, cancellationToken);
	}

	public async ValueTask SetGroupAsync(string path, string group, bool recursive, CancellationToken cancellationToken = default)
	{
		string target = Absolute(path);
		Require(RemoteFileSystemFeatures.Ownership, "change groups", target);
		await RunScriptAsync(RemoteScripts.ChangeGroup, [target, AccountName(group, target), Flag(recursive)], target, cancellationToken);
	}

	/// <summary>
	/// Runs <paramref name="script"/> to its end and returns what it printed after <see cref="RemoteScripts.OutputMarker"/>,
	/// or throws the <see cref="RemoteFileSystemException"/> its exit and stderr describe (see <see cref="RemoteOutput.Require"/>).
	/// </summary>
	/// <param name="path">The path the operation is about, for messages.</param>
	/// <param name="destination">The second path of a move or a copy, for messages.</param>
	/// <param name="allowPartialListing">A listing that could not read every entry still counts, the way <c>ls</c> does.</param>
	protected abstract Task<ReadOnlyMemory<byte>> RunScriptAsync(
		string script,
		IReadOnlyList<string> arguments,
		string path,
		CancellationToken cancellationToken,
		string? destination = null,
		bool allowPartialListing = false);

	/// <summary>
	/// Runs <see cref="RemoteScripts.ReadRange"/> with <paramref name="arguments"/> and returns what it printed after its
	/// marker, or throws what its exit and stderr describe, the way <see cref="RunScriptAsync"/> does.
	/// </summary>
	/// <param name="path">The file, for messages.</param>
	/// <param name="maxOutputBytes">The most the script may print, past which the piece is refused rather than kept.</param>
	protected abstract Task<ReadOnlyMemory<byte>> ReadRangeAsync(IReadOnlyList<string> arguments, string path, int maxOutputBytes, CancellationToken cancellationToken);

	/// <exception cref="RemoteFileSystemException">The file system was closed.</exception>
	protected abstract void ThrowIfDisposed();

	/// <summary>
	/// Why a path cannot be handed to a script, beyond the NUL no command line can carry; null when it can. The default
	/// allows everything else.
	/// </summary>
	protected virtual string? PathProblem(string path) => null;

	/// <summary>The normalized absolute form of <paramref name="path"/>, the way every script gets it.</summary>
	/// <exception cref="RemoteFileSystemException">The file system was closed, or the path cannot be passed on.</exception>
	protected string Absolute(string path)
	{
		ArgumentNullException.ThrowIfNull(path);
		ThrowIfDisposed();
		string normalized = RemotePath.Normalize(path);
		if (normalized.Contains('\0', StringComparison.Ordinal))
		{
			throw new RemoteFileSystemException(RemoteFileErrorKind.NotFound, "Paths cannot contain NUL characters.", path);
		}

		return PathProblem(normalized) is { } problem
			? throw new RemoteFileSystemException(RemoteFileErrorKind.NotSupported, problem, path)
			: normalized;
	}

	/// <summary>
	/// The physical path of <paramref name="absolute"/>, links resolved, or <paramref name="absolute"/> itself when the
	/// script printed nothing usable.
	/// </summary>
	protected async Task<string> ResolveAbsoluteAsync(string absolute, CancellationToken cancellationToken)
	{
		ReadOnlyMemory<byte> payload = await RunScriptAsync(RemoteScripts.Resolve, [absolute], absolute, cancellationToken);
		string resolved = RemoteOutput.ReadLine(payload.Span);
		return resolved.StartsWith('/') ? resolved : absolute;
	}

	/// <summary>The value a script reads as true: <c>1</c>, or empty for false.</summary>
	protected static string Flag(bool value) => value ? "1" : "";

	private Task<PieceReadStream> OpenPiecesAsync(string target, long start, CancellationToken cancellationToken) =>
		PieceReadStream.OpenAsync((from, count, token) => ReadPieceAsync(target, from, count, token), target, start, cancellationToken);

	/// <summary>
	/// Up to <paramref name="count"/> bytes from <paramref name="start"/>: <see cref="RemoteScripts.ReadRange"/> starts at a
	/// whole block, so the bytes before <paramref name="start"/> in that block are read and dropped.
	/// </summary>
	private async Task<FilePiece> ReadPieceAsync(string path, long start, int count, CancellationToken cancellationToken)
	{
		ThrowIfDisposed();
		long block = start / RemoteScripts.ReadBlockBytes;
		int lead = (int)(start % RemoteScripts.ReadBlockBytes);
		int asked = lead + count;
		ReadOnlyMemory<byte> payload = await ReadRangeAsync(
			[path, block.ToString(CultureInfo.InvariantCulture), asked.ToString(CultureInfo.InvariantCulture)],
			path,
			asked + PieceSlackBytes,
			cancellationToken);
		return FilePiece.Parse(payload, path).Skip(lead);
	}

	private static string AccountName(string name, string path) => RemoteAccount.IsValidName(name)
		? name
		: throw new RemoteFileSystemException(RemoteFileErrorKind.Unknown, $"'{name}' is not a valid user or group name.", path);

	private void Require(RemoteFileSystemFeatures feature, string action, string path)
	{
		if ((Features & feature) != feature)
		{
			throw new RemoteFileSystemException(RemoteFileErrorKind.NotSupported, $"This connection cannot {action}.", path);
		}
	}
}
