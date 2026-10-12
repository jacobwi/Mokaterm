using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Modules.Ssh.FileSystem;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// The docker host's own files as the manager reads them: through the session's file system, as the login over SFTP,
/// or as root through the session's one root file system (<see cref="SessionRootFiles"/>, password on stdin only) for
/// the paths only root may read, which is most of <c>/var/lib/docker</c>. Editing does not happen here: the pages hand
/// <see cref="OpenAsync"/>'s file system to the shared editor.
/// </summary>
internal sealed class DockerHostFiles : IAsyncDisposable
{
	/// <summary>The most of a file the manager reads for showing; past it a file is shown cut.</summary>
	public const int MaxViewBytes = 256 * 1024;

	private readonly SessionRootFiles _root;
	private readonly bool _ownsRoot;
	private readonly Func<string> _stagingDirectory;
	private int _disposed;

	/// <summary>Over files of its own, whose root view goes when this does.</summary>
	/// <param name="stagingDirectory">Where uploads are parked on the docker host, read when one starts; null for <c>/tmp</c>.</param>
	public DockerHostFiles(IFileSystemFeature files, Func<string>? stagingDirectory = null)
		: this(new SessionRootFiles(files), ownsRoot: true, stagingDirectory)
	{
	}

	/// <summary>Over the session's root file system, which the session lets go of and this never does.</summary>
	/// <param name="stagingDirectory">Where uploads are parked on the docker host, read when one starts; null for <c>/tmp</c>.</param>
	public DockerHostFiles(SessionRootFiles root, Func<string>? stagingDirectory = null)
		: this(root, ownsRoot: false, stagingDirectory)
	{
	}

	private DockerHostFiles(SessionRootFiles root, bool ownsRoot, Func<string>? stagingDirectory)
	{
		ArgumentNullException.ThrowIfNull(root);
		_root = root;
		_ownsRoot = ownsRoot;
		_stagingDirectory = stagingDirectory ?? (() => "/tmp");
	}

	/// <summary>
	/// The folder on the docker host where an upload waits as the login's private file before it moves on, into a
	/// container for one: the one the SSH settings name for root uploads.
	/// </summary>
	public string StagingDirectory => _stagingDirectory();

	/// <summary>False when the connection does not run anything as root.</summary>
	public bool CanUseRoot => _root.CanUseRoot;

	/// <summary>The login's file system, or the session's one root view.</summary>
	/// <exception cref="RemoteFileSystemException">The session is closed, or sudo refused (<see cref="RemoteFileErrorKind.ElevationFailed"/>).</exception>
	public async Task<IRemoteFileSystem> OpenAsync(bool asRoot, CancellationToken cancellationToken)
	{
		ThrowIfDisposed();
		return asRoot ? await _root.OpenAsync(cancellationToken) : await _root.Files.OpenAsync(cancellationToken);
	}

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

	/// <summary>
	/// Reads a text file for showing, at most <see cref="MaxViewBytes"/> of it: a longer one comes back
	/// <see cref="RemoteTextFile.IsCut"/>, which nothing writes back.
	/// </summary>
	/// <exception cref="RemoteFileSystemException">It is not there, may not be read, or is not text (<see cref="RemoteFileErrorKind.Unknown"/> with a message saying so).</exception>
	public async Task<RemoteTextFile> ReadTextAsync(string path, bool asRoot, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(path);
		IRemoteFileSystem fileSystem = await OpenAsync(asRoot, cancellationToken);
		RemoteTextFile? file;
		await using (Stream stream = await fileSystem.OpenReadAsync(path, cancellationToken))
		{
			(file, _) = await RemoteTextFile.ReadStartAsync(stream, MaxViewBytes, cancellationToken);
		}

		return file ?? throw new RemoteFileSystemException(RemoteFileErrorKind.Unknown, $"{path} is not a text file, so it is not shown.", path);
	}

	/// <summary>Stops reading; a root view of its own goes with it, the session's stays for the session to close.</summary>
	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		if (_ownsRoot)
		{
			await _root.DisposeAsync();
		}
	}

	private void ThrowIfDisposed()
	{
		if (Volatile.Read(ref _disposed) != 0)
		{
			throw new RemoteFileSystemException(RemoteFileErrorKind.ConnectionLost, "The session is closed.");
		}
	}
}
