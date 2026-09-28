using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Diagnostics;
using Mokaterm.Abstractions.Interaction;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace Mokaterm.Modules.Ssh.Tunnels;

/// <summary>
/// The forwarded ports of one session. Opening is deliberately forgiving: a port already in use or a server that
/// refuses to bind leaves the tunnel failed with its reason and the session running.
/// </summary>
internal sealed class SshTunnelManager : ISshTunnelFeature, IAsyncDisposable
{
	private readonly SshClient _client;
	private readonly IUserInteraction? _interaction;
	private readonly ILogger _logger;
	private readonly Guid _sessionId;
	private readonly Lock _lock = new();
	private readonly List<TunnelEntry> _entries = [];
	private readonly SemaphoreSlim _gate = new(1, 1);
	private int _disposed;

	public SshTunnelManager(SshClient client, Guid sessionId, IUserInteraction? interaction, ILogger logger)
	{
		_client = client;
		_sessionId = sessionId;
		_interaction = interaction;
		_logger = logger;
	}

	public event Action? Changed;

	public IReadOnlyList<SshTunnelStatus> Tunnels
	{
		get
		{
			lock (_lock)
			{
				return [.. _entries.Select(entry => entry.ToStatus())];
			}
		}
	}

	public int OpenCount
	{
		get
		{
			lock (_lock)
			{
				return _entries.Count(entry => entry.State == SshTunnelState.Open);
			}
		}
	}

	/// <summary>
	/// Registers the connection's saved tunnels and opens the ones marked to open with the session. Never throws for a
	/// tunnel that cannot open.
	/// </summary>
	public async Task StartSavedAsync(IReadOnlyList<SshTunnelDefinition> saved, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(saved);
		lock (_lock)
		{
			foreach (SshTunnelDefinition definition in saved)
			{
				_entries.Add(new TunnelEntry(definition, isSessionOnly: false));
			}
		}

		List<string> failures = [];
		foreach (SshTunnelDefinition definition in saved.Where(tunnel => tunnel.OpenWithSession))
		{
			SshTunnelStatus status = await OpenAsync(definition.Id, cancellationToken);
			if (status.State == SshTunnelState.Failed)
			{
				failures.Add($"{definition.Display}: {status.Error}");
			}
		}

		if (failures.Count > 0)
		{
			_interaction?.Notify(
				NoticeSeverity.Warning,
				string.Join(Environment.NewLine, failures),
				failures.Count == 1 ? "A tunnel could not open" : $"{failures.Count} tunnels could not open");
		}

		Notify();
	}

	public async Task<SshTunnelStatus> AddAsync(SshTunnelDefinition tunnel, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(tunnel);
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
		if (tunnel.Validate() is { } invalid)
		{
			throw new ArgumentException(invalid, nameof(tunnel));
		}

		lock (_lock)
		{
			_entries.RemoveAll(entry => entry.Definition.Id == tunnel.Id && entry.Port is null);
			_entries.Add(new TunnelEntry(tunnel, isSessionOnly: true));
		}

		Notify();
		return await OpenAsync(tunnel.Id, cancellationToken);
	}

	public async Task<SshTunnelStatus> OpenAsync(Guid tunnelId, CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
		await _gate.WaitAsync(cancellationToken);
		try
		{
			if (Find(tunnelId) is not { } entry)
			{
				throw new KeyNotFoundException($"No tunnel with id {tunnelId}.");
			}

			if (entry.State == SshTunnelState.Open)
			{
				return entry.ToStatus();
			}

			SetState(entry, SshTunnelState.Opening, null, null);
			try
			{
				ForwardedPort port = Create(entry.Definition);
				entry.Attach(port, OnPortException);
				_client.AddForwardedPort(port);

				// Start binds a socket, and a remote tunnel waits for the server's answer, so keep it off the caller.
				await Task.Run(port.Start, cancellationToken);
				SetState(entry, SshTunnelState.Open, null, BoundPortOf(port));
				_logger.LogInformation("SSH session {SessionId} opened tunnel {TunnelId}", _sessionId, tunnelId);
			}
			catch (OperationCanceledException)
			{
				// The port is attached but never started; a later open would otherwise attach a second one beside it.
				await CloseEntryAsync(entry);
				SetState(entry, SshTunnelState.Closed, null, null);
				throw;
			}
			catch (Exception ex)
			{
				await CloseEntryAsync(entry);
				SetState(entry, SshTunnelState.Failed, Describe(ex, entry.Definition), null);
				_logger.LogInformation("SSH session {SessionId} could not open tunnel {TunnelId}: {Error}", _sessionId, tunnelId, LogSafe.Describe(ex));
			}

			return entry.ToStatus();
		}
		finally
		{
			_gate.Release();
			Notify();
		}
	}

	public async Task<SshTunnelStatus> CloseAsync(Guid tunnelId, CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
		await _gate.WaitAsync(cancellationToken);
		try
		{
			if (Find(tunnelId) is not { } entry)
			{
				throw new KeyNotFoundException($"No tunnel with id {tunnelId}.");
			}

			await CloseEntryAsync(entry);
			SetState(entry, SshTunnelState.Closed, null, null);
			return entry.ToStatus();
		}
		finally
		{
			_gate.Release();
			Notify();
		}
	}

	public async Task RemoveAsync(Guid tunnelId, CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
		await _gate.WaitAsync(cancellationToken);
		try
		{
			if (Find(tunnelId) is not { } entry)
			{
				return;
			}

			await CloseEntryAsync(entry);
			lock (_lock)
			{
				_entries.Remove(entry);
			}
		}
		finally
		{
			_gate.Release();
			Notify();
		}
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		TunnelEntry[] entries;
		lock (_lock)
		{
			entries = [.. _entries];
			_entries.Clear();
		}

		// The gate is not disposed: an open still holding it would throw from its finally, and one waiting would wait forever.
		foreach (TunnelEntry entry in entries)
		{
			await CloseEntryAsync(entry);
		}
	}

	/// <summary>A message for a failure to open, in the words the user can act on.</summary>
	internal static string Describe(Exception exception, SshTunnelDefinition tunnel)
	{
		ArgumentNullException.ThrowIfNull(exception);
		ArgumentNullException.ThrowIfNull(tunnel);
		return exception switch
		{
			SocketException { SocketErrorCode: SocketError.AddressAlreadyInUse } =>
				$"Another program already listens on {tunnel.ListenHost}:{tunnel.ListenPort}.",
			SocketException { SocketErrorCode: SocketError.AccessDenied } =>
				$"This account may not listen on {tunnel.ListenHost}:{tunnel.ListenPort}.",
			SocketException { SocketErrorCode: SocketError.AddressNotAvailable } =>
				$"{tunnel.ListenHost} is not an address of this machine.",
			SocketException socket => socket.Message.TrimEnd('.') + ".",
			SshConnectionException => "The SSH connection is closed.",
			SshException when tunnel.Kind == SshTunnelKind.Remote =>
				$"The server refused to listen on {tunnel.ListenHost}:{tunnel.ListenPort}. It may need GatewayPorts, or the port may be taken.",
			SshException ssh => ssh.Message.TrimEnd('.') + ".",
			ObjectDisposedException => "The SSH connection is closed.",
			_ => exception.Message.TrimEnd('.') + ".",
		};
	}

	private static uint BoundPortOf(ForwardedPort port) => port switch
	{
		ForwardedPortLocal local => local.BoundPort,
		ForwardedPortDynamic dynamicPort => dynamicPort.BoundPort,
		ForwardedPortRemote remote => remote.BoundPort,
		_ => 0,
	};

	private static ForwardedPort Create(SshTunnelDefinition tunnel) => tunnel.Kind switch
	{
		SshTunnelKind.Local => new ForwardedPortLocal(tunnel.ListenHost, (uint)tunnel.ListenPort, tunnel.DestinationHost, (uint)tunnel.DestinationPort),
		SshTunnelKind.Remote => new ForwardedPortRemote(tunnel.ListenHost, (uint)tunnel.ListenPort, tunnel.DestinationHost, (uint)tunnel.DestinationPort),
		_ => new ForwardedPortDynamic(tunnel.ListenHost, (uint)tunnel.ListenPort),
	};

	private TunnelEntry? Find(Guid tunnelId)
	{
		lock (_lock)
		{
			return _entries.Find(entry => entry.Definition.Id == tunnelId);
		}
	}

	private void SetState(TunnelEntry entry, SshTunnelState state, string? error, uint? boundPort)
	{
		lock (_lock)
		{
			entry.State = state;
			entry.Error = error;
			entry.BoundPort = boundPort is > 0 ? (int)boundPort.Value : null;
		}
	}

	private async Task CloseEntryAsync(TunnelEntry entry)
	{
		if (entry.Port is not { } port)
		{
			return;
		}

		Detach(entry);
		try
		{
			// Stop waits for pending channels, and dispose sends a cancel request to the server.
			await Task.Run(() =>
			{
				port.Stop();
				port.Dispose();
			});
		}
		catch (Exception ex)
		{
			_logger.LogDebug(ex, "SSH session {SessionId} could not close a tunnel cleanly", _sessionId);
		}
	}

	private void Detach(TunnelEntry entry)
	{
		if (entry.Port is not { } port)
		{
			return;
		}

		port.Exception -= OnPortException;
		entry.Port = null;
		try
		{
			_client.RemoveForwardedPort(port);
		}
		catch (Exception ex) when (ex is ObjectDisposedException or SshConnectionException or InvalidOperationException)
		{
			// The session is already going away, which takes its ports with it.
		}
	}

	private void OnPortException(object? sender, ExceptionEventArgs e)
	{
		if (sender is not ForwardedPort port)
		{
			return;
		}

		TunnelEntry? entry;
		lock (_lock)
		{
			entry = _entries.Find(candidate => ReferenceEquals(candidate.Port, port));
		}

		if (entry is null)
		{
			return;
		}

		// A forwarded port reports the failures of single connections through it too, so a tunnel whose listener is
		// still up stays open and only gets a log line.
		if (port.IsStarted)
		{
			_logger.LogDebug("SSH session {SessionId} saw a failure on a tunnel that is still open: {Error}", _sessionId, LogSafe.Describe(e.Exception));
			return;
		}

		SetState(entry, SshTunnelState.Failed, Describe(e.Exception, entry.Definition), null);
		Notify();
	}

	/// <summary>
	/// Raises <see cref="Changed"/>, one subscriber at a time. SSH.NET reports a port failure from its forwarding threads,
	/// where an exception from a view would end the thread and with it the process.
	/// </summary>
	private void Notify()
	{
		if (Changed is not { } handlers)
		{
			return;
		}

		foreach (Delegate handler in handlers.GetInvocationList())
		{
			try
			{
				((Action)handler)();
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "A tunnel change handler of SSH session {SessionId} threw", _sessionId);
			}
		}
	}

	private sealed class TunnelEntry
	{
		public TunnelEntry(SshTunnelDefinition definition, bool isSessionOnly)
		{
			Definition = definition;
			IsSessionOnly = isSessionOnly;
		}

		public SshTunnelDefinition Definition { get; }

		public bool IsSessionOnly { get; }

		public SshTunnelState State { get; set; }

		public string? Error { get; set; }

		public int? BoundPort { get; set; }

		public ForwardedPort? Port { get; set; }

		public void Attach(ForwardedPort port, EventHandler<ExceptionEventArgs> onException)
		{
			Port = port;
			port.Exception += onException;
		}

		public SshTunnelStatus ToStatus() => new()
		{
			Tunnel = Definition,
			State = State,
			Error = Error,
			BoundPort = BoundPort,
			IsSessionOnly = IsSessionOnly,
		};
	}
}
