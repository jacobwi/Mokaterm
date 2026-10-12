using System.Text;
using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.UI.FileBrowser.Tests.Fakes;

/// <summary>
/// Files held as bytes, each with a modification time that moves on every write, which is all the editor's save looks
/// at. <see cref="WriteElsewhere"/> stands for someone else changing a file on the server.
/// </summary>
internal sealed class TextFileSystem : IRemoteFileSystem
{
	private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

	private readonly Dictionary<string, (byte[] Bytes, DateTimeOffset Modified)> _files = new(StringComparer.Ordinal);
	private int _clock;

	public RemoteFileSystemFeatures Features => RemoteFileSystemFeatures.None;

	public string UserName => "abc";

	public bool IsElevated => false;

	/// <summary>What the editor wrote, in order, decoded as UTF-8.</summary>
	public List<string> Uploads { get; } = [];

	/// <summary>Failures for the next uploads, in order; an upload with none left succeeds.</summary>
	public Queue<Exception> UploadFailures { get; } = new();

	/// <summary>When set, opening a file for reading fails with this.</summary>
	public Exception? ReadFailure { get; set; }

	public TextFileSystem WriteElsewhere(string path, string text) => WriteElsewhere(path, Encoding.UTF8.GetBytes(text));

	public TextFileSystem WriteElsewhere(string path, byte[] bytes)
	{
		_files[path] = (bytes, Epoch.AddMinutes(++_clock));
		return this;
	}

	public void DeleteElsewhere(string path) => _files.Remove(path);

	public string Read(string path) => Encoding.UTF8.GetString(_files[path].Bytes);

	public ValueTask<RemoteFileEntry?> StatAsync(string path, CancellationToken cancellationToken = default) =>
		ValueTask.FromResult(_files.TryGetValue(path, out (byte[] Bytes, DateTimeOffset Modified) file)
			? new RemoteFileEntry
			{
				Name = RemotePath.GetName(path),
				Path = path,
				Kind = RemoteEntryKind.File,
				Size = file.Bytes.Length,
				LastModified = file.Modified,
			}
			: null);

	public ValueTask<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default)
	{
		if (ReadFailure is { } failure)
		{
			return ValueTask.FromException<Stream>(failure);
		}

		return _files.TryGetValue(path, out (byte[] Bytes, DateTimeOffset Modified) file)
			? ValueTask.FromResult<Stream>(new MemoryStream(file.Bytes, writable: false))
			: ValueTask.FromException<Stream>(new RemoteFileSystemException(RemoteFileErrorKind.NotFound, "No such file", path));
	}

	public async ValueTask UploadAsync(string path, Stream source, UploadOptions options, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
	{
		if (UploadFailures.TryDequeue(out Exception? failure))
		{
			throw failure;
		}

		if (!options.Overwrite && _files.ContainsKey(path))
		{
			throw new RemoteFileSystemException(RemoteFileErrorKind.AlreadyExists, "File exists", path);
		}

		using MemoryStream copy = new();
		await source.CopyToAsync(copy, cancellationToken);
		byte[] bytes = copy.ToArray();
		Uploads.Add(Encoding.UTF8.GetString(bytes));
		WriteElsewhere(path, bytes);
	}

	public ValueTask<string> GetHomeDirectoryAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult("/home/abc");

	public ValueTask<string> ResolvePathAsync(string path, CancellationToken cancellationToken = default) => ValueTask.FromResult(path);

	public ValueTask<IReadOnlyList<RemoteFileEntry>> ListAsync(string path, CancellationToken cancellationToken = default) =>
		throw new NotSupportedException();

	public ValueTask CreateDirectoryAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();

	public ValueTask DeleteAsync(string path, bool recursive, CancellationToken cancellationToken = default) => throw new NotSupportedException();

	public ValueTask RenameAsync(string sourcePath, string destinationPath, bool overwrite, CancellationToken cancellationToken = default) =>
		throw new NotSupportedException();

	public ValueTask SetPermissionsAsync(string path, UnixFileMode permissions, bool recursive, CancellationToken cancellationToken = default) =>
		throw new NotSupportedException();

	public ValueTask SetOwnerAsync(string path, string owner, string? group, bool recursive, CancellationToken cancellationToken = default) =>
		throw new NotSupportedException();

	public ValueTask DownloadAsync(string path, Stream destination, IProgress<long>? progress = null, CancellationToken cancellationToken = default) =>
		throw new NotSupportedException();

	public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
