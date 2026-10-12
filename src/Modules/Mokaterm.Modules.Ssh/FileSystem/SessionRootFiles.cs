using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.Modules.Ssh.FileSystem;

/// <summary>
/// The one root file system a session's tools share (the Docker manager, the system view): a root view from
/// <see cref="IFileSystemFeature.OpenElevatedAsync"/>, opened the first time a tool needs it, which may ask for the sudo
/// password, and kept until whoever made this lets go of it. Every root view holds a sudo runner with the password in
/// it, so one per session is the most there should be.
/// </summary>
internal sealed class SessionRootFiles : IAsyncDisposable
{
	private readonly SemaphoreSlim _gate = new(1, 1);
	private IRemoteFileSystem? _root;
	private int _disposed;

	public SessionRootFiles(IFileSystemFeature files)
	{
		ArgumentNullException.ThrowIfNull(files);
		Files = files;
	}

	/// <summary>The session's files, which the login's own view comes from.</summary>
	public IFileSystemFeature Files { get; }

	/// <summary>False when the connection does not run anything as root.</summary>
	public bool CanUseRoot => Files.SupportsElevation;

	/// <summary>
	/// The root view, opened on the first call. One that fails to open is not kept, so the next call asks again, and
	/// calls that arrive together share the one that opens.
	/// </summary>
	/// <exception cref="RemoteFileSystemException">The session is closed, or sudo refused (<see cref="RemoteFileErrorKind.ElevationFailed"/>).</exception>
	/// <exception cref="NotSupportedException"><see cref="CanUseRoot"/> is false.</exception>
	public async Task<IRemoteFileSystem> OpenAsync(CancellationToken cancellationToken)
	{
		ThrowIfDisposed();
		if (Volatile.Read(ref _root) is { } current)
		{
			return current;
		}

		await _gate.WaitAsync(cancellationToken);
		try
		{
			ThrowIfDisposed();
			if (Volatile.Read(ref _root) is { } open)
			{
				return open;
			}

			IRemoteFileSystem root = await Files.OpenElevatedAsync(cancellationToken);
			Volatile.Write(ref _root, root);
			if (Volatile.Read(ref _disposed) != 0)
			{
				// Let go of while sudo was being asked for. Whichever side takes the view out of the field disposes it.
				if (Interlocked.Exchange(ref _root, null) is { } late)
				{
					await late.DisposeAsync();
				}

				ThrowIfDisposed();
			}

			return root;
		}
		finally
		{
			_gate.Release();
		}
	}

	/// <summary>
	/// Lets go of the root view. It does not wait for one still opening, which may sit behind a sudo prompt: that one is
	/// disposed where it lands.
	/// </summary>
	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		if (Interlocked.Exchange(ref _root, null) is { } root)
		{
			await root.DisposeAsync();
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
