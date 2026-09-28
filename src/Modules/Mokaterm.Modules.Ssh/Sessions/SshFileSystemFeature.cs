using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Modules.Ssh.Elevation;
using Mokaterm.Modules.Ssh.FileSystem;
using LoginMethod = Mokaterm.Abstractions.Connections.AuthenticationMethod;

namespace Mokaterm.Modules.Ssh.Sessions;

/// <summary>
/// Files for ssh and sftp sessions: the user's SFTP file system, opened once and shared, and root views through sudo, one
/// per <see cref="OpenElevatedAsync"/> call. The feature tracks every view it hands out and disposes them with the session.
/// </summary>
internal sealed class SshFileSystemFeature : IFileSystemFeature, IAsyncDisposable
{
	private readonly ISshSessionHost _host;
	private readonly ILogger _logger;
	private readonly SemaphoreSlim _gate = new(1, 1);
	private readonly Lock _elevatedLock = new();
	private readonly List<SudoFileSystem> _elevated = [];
	private SftpFileSystem? _fileSystem;
	private int _disposed;

	public SshFileSystemFeature(ISshSessionHost host, ILogger logger)
	{
		_host = host;
		_logger = logger;
	}

	public bool SupportsElevation => _host.Context.Options.Elevation != SshElevationMode.None;

	public async ValueTask<IRemoteFileSystem> OpenAsync(CancellationToken cancellationToken = default) =>
		await OpenUserFileSystemAsync(cancellationToken);

	public async ValueTask<IRemoteFileSystem> OpenElevatedAsync(CancellationToken cancellationToken = default)
	{
		if (!SupportsElevation)
		{
			throw new NotSupportedException("Running as root is turned off for this connection.");
		}

		ThrowIfDisposed();
		SshSettings settings = _host.Connector.Settings;
		SecuritySettings security = _host.Connector.SettingsService.Get<SecuritySettings>();
		SudoLogin login = new()
		{
			Client = await _host.GetSshClientAsync(cancellationToken),
			SudoCommand = settings.EffectiveSudoCommand,
			Account = _host.Context.Account,
			LoginPassword = _host.Context.Credentials.Method is LoginMethod.Password or LoginMethod.KeyboardInteractive
				? _host.Context.Credentials.Password
				: null,
			Cache = _host.SudoPasswords,
			Interaction = _host.Context.Interaction,
			OfferRemember = security.RememberSudoPasswordForSession,
		};

		SudoRunner runner = await SudoRunner.AuthenticateAsync(login, cancellationToken);
		SudoFileSystem fileSystem = new(runner, OpenUserFileSystemAsync, _host.AccountNames, settings.EffectiveStagingDirectory, _logger, Forget);
		lock (_elevatedLock)
		{
			_elevated.Add(fileSystem);
		}

		if (Volatile.Read(ref _disposed) != 0)
		{
			await fileSystem.DisposeAsync();
			ThrowIfDisposed();
		}

		return fileSystem;
	}

	/// <summary>The shared user file system, reopened when its connection dropped.</summary>
	public async ValueTask<SftpFileSystem> OpenUserFileSystemAsync(CancellationToken cancellationToken)
	{
		ThrowIfDisposed();
		await _gate.WaitAsync(cancellationToken);
		try
		{
			ThrowIfDisposed();
			if (_fileSystem is { IsUsable: true } current)
			{
				return current;
			}

			if (_fileSystem is not null)
			{
				await _fileSystem.CloseAsync();
				_fileSystem = null;
			}

			SftpFileSystemOptions options = new()
			{
				UserName = _host.Context.Credentials.Username,
				InitialDirectory = _host.Context.Options.InitialDirectory,
				SupportsElevation = SupportsElevation,
			};

			_fileSystem = new SftpFileSystem(await _host.GetSftpClientAsync(cancellationToken), options, _host.AccountNames, () => _host.ConnectedSshClient, _logger);
			return _fileSystem;
		}
		finally
		{
			_gate.Release();
		}
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		SudoFileSystem[] elevated;
		lock (_elevatedLock)
		{
			elevated = [.. _elevated];
			_elevated.Clear();
		}

		foreach (SudoFileSystem fileSystem in elevated)
		{
			await fileSystem.DisposeAsync();
		}

		if (Interlocked.Exchange(ref _fileSystem, null) is { } userFileSystem)
		{
			await userFileSystem.CloseAsync();
		}
	}

	private void Forget(SudoFileSystem fileSystem)
	{
		lock (_elevatedLock)
		{
			_elevated.Remove(fileSystem);
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
