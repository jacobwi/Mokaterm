using System.Buffers;
using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// The docker host's own files as the manager reads them: through the session's file system, as the login over SFTP,
/// or as root through the module's sudo file system (password on stdin only) for the paths only root may read, which is
/// most of <c>/var/lib/docker</c>. Writing goes through the same file system's replace, which keeps the file's owner
/// and mode, and refuses a file that changed since it was read.
/// </summary>
internal sealed class DockerHostFiles
{
	/// <summary>The most of a file the viewer and the env editor read; past it a file is shown cut and never written.</summary>
	public const int MaxTextBytes = 256 * 1024;

	private readonly IFileSystemFeature _files;

	public DockerHostFiles(IFileSystemFeature files)
	{
		ArgumentNullException.ThrowIfNull(files);
		_files = files;
	}

	/// <summary>False when the connection does not run anything as root.</summary>
	public bool CanUseRoot => _files.SupportsElevation;

	/// <summary>What is at <paramref name="path"/>, or null when nothing is.</summary>
	public async Task<RemoteFileEntry?> StatAsync(string path, bool asRoot, CancellationToken cancellationToken)
	{
		IRemoteFileSystem fileSystem = await OpenAsync(asRoot, cancellationToken);
		return await fileSystem.StatAsync(path, cancellationToken);
	}

	public async Task<IReadOnlyList<RemoteFileEntry>> ListAsync(string directory, bool asRoot, CancellationToken cancellationToken)
	{
		IRemoteFileSystem fileSystem = await OpenAsync(asRoot, cancellationToken);
		return await fileSystem.ListAsync(directory, cancellationToken);
	}

	/// <summary>Reads a text file, at most <see cref="MaxTextBytes"/> of it.</summary>
	/// <exception cref="RemoteFileSystemException">It is not there, may not be read, or is not text (<see cref="RemoteFileErrorKind.Unknown"/> with a message saying so).</exception>
	public async Task<HostTextFile> ReadTextAsync(string path, bool asRoot, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(path);
		IRemoteFileSystem fileSystem = await OpenAsync(asRoot, cancellationToken);
		RemoteFileEntry entry = await fileSystem.StatAsync(path, cancellationToken)
			?? throw new RemoteFileSystemException(RemoteFileErrorKind.NotFound, $"{path} does not exist.", path);
		if (entry.IsDirectoryLike)
		{
			throw new RemoteFileSystemException(RemoteFileErrorKind.Unknown, $"{path} is a folder.", path);
		}

		byte[] buffer = ArrayPool<byte>.Shared.Rent(MaxTextBytes + 1);
		try
		{
			int total = 0;
			await using (Stream stream = await fileSystem.OpenReadAsync(path, cancellationToken))
			{
				int read;
				while (total < MaxTextBytes + 1 && (read = await stream.ReadAsync(buffer.AsMemory(total, MaxTextBytes + 1 - total), cancellationToken)) > 0)
				{
					total += read;
				}
			}

			bool truncated = total > MaxTextBytes;
			ReadOnlySpan<byte> bytes = buffer.AsSpan(0, Math.Min(total, MaxTextBytes));
			string text = HostTextFile.Decode(bytes, truncated, out bool hasByteOrderMark)
				?? throw new RemoteFileSystemException(RemoteFileErrorKind.Unknown, $"{path} is not a text file, so it is not shown.", path);

			return new HostTextFile
			{
				Path = path,
				Text = text.ReplaceLineEndings("\n"),
				Size = entry.Size,
				Modified = entry.LastModified,
				Truncated = truncated,
				HasByteOrderMark = hasByteOrderMark,
				UsesCrLf = text.Contains("\r\n", StringComparison.Ordinal),
				ReadAsRoot = asRoot,
			};
		}
		finally
		{
			// An env file is where passwords live, so its bytes do not stay behind in the pool.
			ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
		}
	}

	/// <summary>
	/// Writes <paramref name="text"/> over the file <paramref name="original"/> came from, with the replace the file
	/// browser's editor uses (a temporary file with the old owner and mode, renamed over it).
	/// </summary>
	/// <exception cref="RemoteFileSystemException">It changed or went away since it was read, or may not be written.</exception>
	public async Task WriteTextAsync(HostTextFile original, string text, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(original);
		ArgumentNullException.ThrowIfNull(text);
		if (original.Truncated)
		{
			throw new RemoteFileSystemException(RemoteFileErrorKind.Unknown, $"Only the start of {original.Path} was read, so it is not written back.", original.Path);
		}

		IRemoteFileSystem fileSystem = await OpenAsync(original.ReadAsRoot, cancellationToken);
		RemoteFileEntry? now = await fileSystem.StatAsync(original.Path, cancellationToken);
		if (now is null || now.Size != original.Size || now.LastModified != original.Modified)
		{
			throw new RemoteFileSystemException(
				RemoteFileErrorKind.Unknown,
				$"{original.Path} changed on the server since it was opened. Open it again to see the new version.",
				original.Path);
		}

		using MemoryStream content = new(original.Encode(text));
		await fileSystem.UploadAsync(original.Path, content, new UploadOptions { Overwrite = true }, progress: null, cancellationToken);
	}

	private async Task<IRemoteFileSystem> OpenAsync(bool asRoot, CancellationToken cancellationToken) =>
		asRoot ? await _files.OpenElevatedAsync(cancellationToken) : await _files.OpenAsync(cancellationToken);
}
