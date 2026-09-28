using System.Buffers;
using System.Security.Cryptography;
using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.DevHost.Demo.FileSystem;

/// <summary>
/// The file browser's view of a session's <see cref="DemoFileTree"/> as one account. Every call waits a little, like a
/// server across a network, and transfers run at about 20 MB/s.
/// </summary>
internal sealed class DemoFileSystem : IRemoteFileSystem
{
	private const int MinLatencyMilliseconds = 40;
	private const int MaxLatencyMilliseconds = 120;
	private const int TransferBufferSize = 80 * 1024;

	private readonly DemoFileTree _tree;
	private readonly DemoAccount _account;
	private readonly CancellationToken _sessionClosed;

	public DemoFileSystem(DemoFileTree tree, DemoAccount account, bool isElevated, CancellationToken sessionClosed)
	{
		_tree = tree;
		_account = account;
		_sessionClosed = sessionClosed;
		IsElevated = isElevated;
	}

	public RemoteFileSystemFeatures Features =>
		RemoteFileSystemFeatures.Permissions | RemoteFileSystemFeatures.Ownership | RemoteFileSystemFeatures.SymbolicLinks | RemoteFileSystemFeatures.Elevation;

	public string UserName => _account.Name;

	public bool IsElevated { get; }

	public async ValueTask<string> GetHomeDirectoryAsync(CancellationToken cancellationToken = default)
	{
		await LatencyAsync(cancellationToken);
		return _tree.Stat(_account.Home, _account) is { IsDirectoryLike: true } ? _account.Home : RemotePath.Root;
	}

	public async ValueTask<string> ResolvePathAsync(string path, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(path);
		await LatencyAsync(cancellationToken);

		// Relative paths start in the home folder, where an SFTP session starts.
		string absolute = path switch
		{
			"" or "~" => _account.Home,
			_ when path.StartsWith("~/", StringComparison.Ordinal) => RemotePath.Combine(_account.Home, path[2..]),
			_ => RemotePath.Combine(_account.Home, path),
		};

		return _tree.Resolve(absolute, _account);
	}

	public async ValueTask<IReadOnlyList<RemoteFileEntry>> ListAsync(string path, CancellationToken cancellationToken = default)
	{
		await LatencyAsync(cancellationToken);
		return _tree.ListDirectory(path, _account);
	}

	public async ValueTask<RemoteFileEntry?> StatAsync(string path, CancellationToken cancellationToken = default)
	{
		await LatencyAsync(cancellationToken);
		return _tree.Stat(path, _account);
	}

	public async ValueTask CreateDirectoryAsync(string path, CancellationToken cancellationToken = default)
	{
		await LatencyAsync(cancellationToken);
		_tree.CreateDirectory(path, _account);
	}

	public async ValueTask DeleteAsync(string path, bool recursive, CancellationToken cancellationToken = default)
	{
		await LatencyAsync(cancellationToken);
		_tree.Delete(path, recursive, _account);
	}

	public async ValueTask RenameAsync(string sourcePath, string destinationPath, bool overwrite, CancellationToken cancellationToken = default)
	{
		await LatencyAsync(cancellationToken);
		_tree.Rename(sourcePath, destinationPath, overwrite, _account);
	}

	public async ValueTask SetPermissionsAsync(string path, UnixFileMode permissions, bool recursive, CancellationToken cancellationToken = default)
	{
		await LatencyAsync(cancellationToken);
		_tree.SetPermissions(path, permissions, recursive, _account);
	}

	public async ValueTask SetOwnerAsync(string path, string owner, string? group, bool recursive, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(owner);
		await LatencyAsync(cancellationToken);
		_tree.SetOwner(path, owner, group, recursive, _account);
	}

	public async ValueTask<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default)
	{
		await LatencyAsync(cancellationToken);
		DemoFileData file = _tree.Read(path, _account);
		return file.IsComplete
			? new MemoryStream(file.Content, writable: false)
			: new GeneratedStream(file.Size, file.Content);
	}

	public async ValueTask UploadAsync(string path, Stream source, UploadOptions options, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(options);
		await LatencyAsync(cancellationToken);

		// Created before the first byte, like an SFTP open: a failed upload leaves its partial file behind.
		DemoNode file = _tree.BeginWrite(path, options.Overwrite, options.Permissions, _account);
		await CopyPacedAsync(
			source,
			(chunk, _) =>
			{
				_tree.Append(file, chunk.Span);
				return ValueTask.CompletedTask;
			},
			progress,
			cancellationToken);

		_tree.CompleteWrite(file, options.LastModified);
	}

	public async ValueTask DownloadAsync(string path, Stream destination, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(destination);
		Stream source = await OpenReadAsync(path, cancellationToken);
		await using (source)
		{
			await CopyPacedAsync(source, destination.WriteAsync, progress, cancellationToken);
		}
	}

	/// <summary>The session owns its views and ends them by closing; disposing one does nothing.</summary>
	public ValueTask DisposeAsync() => ValueTask.CompletedTask;

	private async Task CopyPacedAsync(
		Stream source,
		Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask> write,
		IProgress<long>? progress,
		CancellationToken cancellationToken)
	{
		TransferPace pace = new();
		byte[] buffer = ArrayPool<byte>.Shared.Rent(TransferBufferSize);
		try
		{
			long total = 0;
			int read;
			while ((read = await source.ReadAsync(buffer.AsMemory(0, TransferBufferSize), cancellationToken)) > 0)
			{
				ThrowIfClosed();
				await write(buffer.AsMemory(0, read), cancellationToken);
				total += read;
				progress?.Report(total);
				await pace.WaitAsync(total, cancellationToken);
			}
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(buffer);
		}
	}

	private async Task LatencyAsync(CancellationToken cancellationToken)
	{
		ThrowIfClosed();
		int milliseconds = RandomNumberGenerator.GetInt32(MinLatencyMilliseconds, MaxLatencyMilliseconds + 1);
		await Task.Delay(milliseconds, cancellationToken);
		ThrowIfClosed();
	}

	private void ThrowIfClosed()
	{
		if (_sessionClosed.IsCancellationRequested)
		{
			throw new RemoteFileSystemException(RemoteFileErrorKind.ConnectionLost, "The session is closed.");
		}
	}
}
