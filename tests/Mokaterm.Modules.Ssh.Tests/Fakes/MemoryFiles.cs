using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.Modules.Ssh.Tests.Fakes;

/// <summary>The docker host's files as a table of paths, for reads, and for uploads parked there on their way somewhere else.</summary>
internal sealed class MemoryFiles(bool elevated) : IRemoteFileSystem
{
	public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);

	/// <summary>Every upload, with the options it was asked for, in order.</summary>
	public List<(string Path, UploadOptions Options)> Uploads { get; } = [];

	public bool Disposed { get; private set; }

	public RemoteFileSystemFeatures Features => RemoteFileSystemFeatures.None;

	public string UserName => elevated ? "root" : "deploy";

	public bool IsElevated => elevated;

	public ValueTask<RemoteFileEntry?> StatAsync(string path, CancellationToken cancellationToken = default) =>
		ValueTask.FromResult<RemoteFileEntry?>(null);

	public ValueTask<IReadOnlyList<RemoteFileEntry>> ListAsync(string path, CancellationToken cancellationToken = default) =>
		ValueTask.FromResult<IReadOnlyList<RemoteFileEntry>>([]);

	public ValueTask<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default) =>
		Files.TryGetValue(path, out byte[]? bytes)
			? ValueTask.FromResult<Stream>(new MemoryStream(bytes, writable: false))
			: ValueTask.FromException<Stream>(new RemoteFileSystemException(RemoteFileErrorKind.NotFound, "No such file", path));

	public ValueTask DisposeAsync()
	{
		Disposed = true;
		return ValueTask.CompletedTask;
	}

	public ValueTask<string> GetHomeDirectoryAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult("/home/deploy");

	public ValueTask<string> ResolvePathAsync(string path, CancellationToken cancellationToken = default) => ValueTask.FromResult(path);

	public ValueTask CreateDirectoryAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();

	public ValueTask DeleteAsync(string path, bool recursive, CancellationToken cancellationToken = default) =>
		Files.Remove(path)
			? ValueTask.CompletedTask
			: ValueTask.FromException(new RemoteFileSystemException(RemoteFileErrorKind.NotFound, "No such file", path));

	public ValueTask RenameAsync(string sourcePath, string destinationPath, bool overwrite, CancellationToken cancellationToken = default) =>
		throw new NotSupportedException();

	public ValueTask SetPermissionsAsync(string path, UnixFileMode permissions, bool recursive, CancellationToken cancellationToken = default) =>
		throw new NotSupportedException();

	public ValueTask SetOwnerAsync(string path, string owner, string? group, bool recursive, CancellationToken cancellationToken = default) =>
		throw new NotSupportedException();

	public async ValueTask UploadAsync(string path, Stream source, UploadOptions options, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
	{
		using MemoryStream copy = new();
		await source.CopyToAsync(copy, cancellationToken);
		Files[path] = copy.ToArray();
		Uploads.Add((path, options));
		progress?.Report(copy.Length);
	}

	public ValueTask DownloadAsync(string path, Stream destination, IProgress<long>? progress = null, CancellationToken cancellationToken = default) =>
		throw new NotSupportedException();
}
