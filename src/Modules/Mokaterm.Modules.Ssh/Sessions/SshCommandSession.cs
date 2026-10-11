using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Diagnostics;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Ssh.Connection;
using Mokaterm.Modules.Ssh.Shell;
using Renci.SshNet;

namespace Mokaterm.Modules.Ssh.Sessions;

/// <summary>
/// A connection opened only to run commands (<see cref="ConnectPurpose.Commands"/>): the login, jump hosts and proxy
/// a tab would get, without the shell, the saved tunnels or the startup command. Each command takes an exec channel of
/// its own on this connection, and disposing closes the connection, the hops and the login with it.
/// </summary>
internal sealed class SshCommandSession : IProtocolSession
{
	private readonly SshClient _client;
	private readonly SshSessionContext _context;
	private readonly ConnectionMonitor _monitor;
	private readonly SshCommandFeature _commands;
	private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly ILogger _logger;
	private int _disposed;

	/// <summary>Takes ownership of <paramref name="connection"/>.</summary>
	public SshCommandSession(SshConnection<SshClient> connection, TimeProvider time, ILogger logger)
	{
		ArgumentNullException.ThrowIfNull(connection);
		_client = connection.Client;
		_context = connection.Context;
		_logger = logger;
		_monitor = new ConnectionMonitor(_client);
		_commands = new SshCommandFeature(GetClientAsync, time);
		_ = WatchAsync();
	}

	public Task Completion => _completion.Task;

	public TFeature? GetFeature<TFeature>()
		where TFeature : class =>
		typeof(TFeature) == typeof(IRemoteCommandFeature) ? (TFeature)(object)_commands : null;

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		_completion.TrySetResult();

		// Each part closes even when one before it fails: the connection, the jump hosts and the login must not outlive the session.
		Guid id = _context.SessionId;
		await SessionTeardown.ReleaseAsync(_monitor.DisposeAsync, _logger, id, "the connection monitor");
		await SessionTeardown.ReleaseAsync(() => new ValueTask(CompanionConnection<SshClient>.DisposeClientAsync(_client)), _logger, id, "the SSH connection");
		await SessionTeardown.ReleaseAsync(_context.DisposeAsync, _logger, id, "the login and jump hosts");
		_logger.LogInformation("SSH command session {SessionId} closed", id);
	}

	private Task<SshClient> GetClientAsync(CancellationToken cancellationToken) =>
		Volatile.Read(ref _disposed) == 0 && ConnectionMonitor.IsConnected(_client)
			? Task.FromResult(_client)
			: Task.FromException<SshClient>(new RemoteCommandException("The SSH connection is closed."));

	private async Task WatchAsync()
	{
		Exception cause = await _monitor.Lost;
		if (_completion.TrySetException(cause))
		{
			_logger.LogInformation("SSH command session {SessionId} lost its connection: {Failure}", _context.SessionId, LogSafe.Describe(cause));
		}
	}
}
