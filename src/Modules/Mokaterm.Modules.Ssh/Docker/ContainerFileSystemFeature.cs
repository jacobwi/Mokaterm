using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// One container's files as the file browser asks for them: opened the first time, kept while the session lives, and
/// asked again by every transfer that runs, as any session's file system is. The container is found by name, so a
/// container compose recreated under the same name is still the one browsed.
/// </summary>
internal sealed class ContainerFileSystemFeature : IFileSystemFeature, IAsyncDisposable
{
	private readonly Func<DockerClient> _client;
	private readonly DockerHostFiles? _hostFiles;
	private readonly ILogger? _logger;
	private readonly SemaphoreSlim _gate = new(1, 1);
	private ContainerFileSystem? _open;
	private int _disposed;

	/// <param name="client">The manager's client as it is at each call, so a switch to sudo applies to files too.</param>
	/// <param name="hostFiles">The docker host's files, which uploads are staged through; null when the session has none.</param>
	public ContainerFileSystemFeature(Func<DockerClient> client, string container, DockerHostFiles? hostFiles, ILogger? logger)
	{
		ArgumentNullException.ThrowIfNull(client);
		ArgumentNullException.ThrowIfNull(container);
		_client = client;
		Container = container;
		_hostFiles = hostFiles;
		_logger = logger;
	}

	public string Container { get; }

	/// <summary>
	/// False: inside a container the files are reached as the container's own user, and the session's sudo is about the
	/// docker host, not about anything in here.
	/// </summary>
	public bool SupportsElevation => false;

	public async ValueTask<IRemoteFileSystem> OpenAsync(CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();
		await _gate.WaitAsync(cancellationToken);
		try
		{
			ThrowIfDisposed();
			ContainerFileSystem open = _open ??= await ContainerFileSystem.OpenAsync(_client, Container, _hostFiles, _logger, cancellationToken);

			// Closed while the container was being asked: the dispose found nothing to close, so this one closes itself.
			if (Volatile.Read(ref _disposed) != 0)
			{
				await open.DisposeAsync();
				ThrowIfDisposed();
			}

			return open;
		}
		finally
		{
			_gate.Release();
		}
	}

	public ValueTask<IRemoteFileSystem> OpenElevatedAsync(CancellationToken cancellationToken = default) =>
		throw new NotSupportedException("A container's files are reached as the container's own user.");

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		if (Volatile.Read(ref _open) is { } open)
		{
			await open.DisposeAsync();
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
