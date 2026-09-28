using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Ftp.FileSystem;

namespace Mokaterm.Modules.Ftp.Connection;

/// <summary>
/// A logged-in FTP connection. It exposes only <see cref="IFileSystemFeature"/>, keeps a copy of the credentials and the
/// certificate trust for the extra transfer connections, and runs the keepalive that detects a dead connection.
/// </summary>
internal sealed class FtpSession : IProtocolSession, IFileSystemFeature
{
	private const int MaxKeepAliveFailures = 3;

	private readonly FtpFileSystem _fileSystem;
	private readonly FtpCertificateTrust _trust;
	private readonly LoginCredentials _credentials;
	private readonly TimeSpan _keepAliveInterval;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger _logger;
	private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly CancellationTokenSource _lifetime = new();
	private Task _keepAlive = Task.CompletedTask;
	private int _disposed;

	/// <param name="credentials">A copy the session owns and disposes.</param>
	/// <param name="keepAliveInterval">Idle time before a NOOP; zero turns keepalive off.</param>
	public FtpSession(FtpFileSystem fileSystem, FtpCertificateTrust trust, LoginCredentials credentials, TimeSpan keepAliveInterval, TimeProvider timeProvider, ILogger logger)
	{
		_fileSystem = fileSystem;
		_trust = trust;
		_credentials = credentials;
		_keepAliveInterval = keepAliveInterval;
		_timeProvider = timeProvider;
		_logger = logger;
	}

	public Task Completion => _completion.Task;

	public bool SupportsElevation => false;

	public void Start()
	{
		if (_keepAliveInterval > TimeSpan.Zero)
		{
			_keepAlive = RunKeepAliveAsync(_lifetime.Token);
		}
	}

	public TFeature? GetFeature<TFeature>() where TFeature : class =>
		typeof(TFeature) == typeof(IFileSystemFeature) ? (TFeature)(object)this : null;

	public ValueTask<IRemoteFileSystem> OpenAsync(CancellationToken cancellationToken = default)
	{
		if (Volatile.Read(ref _disposed) != 0)
		{
			throw new RemoteFileSystemException(RemoteFileErrorKind.ConnectionLost, "The FTP session is closed.");
		}

		return ValueTask.FromResult<IRemoteFileSystem>(_fileSystem);
	}

	public ValueTask<IRemoteFileSystem> OpenElevatedAsync(CancellationToken cancellationToken = default) =>
		throw new NotSupportedException("FTP cannot run file operations as root.");

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		await _lifetime.CancelAsync();
		await _keepAlive;
		await _fileSystem.CloseAsync();
		_trust.Dispose();
		_credentials.Dispose();
		_lifetime.Dispose();
		_completion.TrySetResult();
	}

	private async Task RunKeepAliveAsync(CancellationToken cancellationToken)
	{
		int failures = 0;
		try
		{
			using PeriodicTimer timer = new(_keepAliveInterval, _timeProvider);
			while (await timer.WaitForNextTickAsync(cancellationToken))
			{
				try
				{
					// Only an answered NOOP clears the count: a skipped one (the connection was busy or just used) proves nothing.
					if (await _fileSystem.KeepAliveAsync(_keepAliveInterval, cancellationToken))
					{
						failures = 0;
					}
				}
				catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
				{
					// The keepalive already reconnects a dropped connection; a failure means the server stayed unreachable.
					failures++;
					_logger.LogWarning("FTP keepalive failed ({Failures} of {Limit}): {Error}", failures, MaxKeepAliveFailures, ex.InnerException?.GetType().Name ?? ex.GetType().Name);
					if (failures >= MaxKeepAliveFailures)
					{
						_completion.TrySetException(new RemoteFileSystemException(RemoteFileErrorKind.ConnectionLost, "The connection to the FTP server was lost.", innerException: ex));
						return;
					}
				}
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			// The session is closing.
		}
	}
}
