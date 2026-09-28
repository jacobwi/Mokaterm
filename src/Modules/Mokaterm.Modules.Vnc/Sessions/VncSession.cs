using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Vnc.Protocol;

namespace Mokaterm.Modules.Vnc.Sessions;

/// <summary>
/// A VNC session. It exposes only <see cref="IVncConnection"/> and keeps the connector and a copy of the login so
/// the screen can come back after its view was unmounted, which happens whenever the vault locks.
/// </summary>
internal sealed class VncSession : IProtocolSession, IVncConnection
{
	private readonly VncConnector _connector;
	private readonly LoginCredentials? _credentials;
	private readonly ILogger _logger;
	private readonly SemaphoreSlim _attachLock = new(1, 1);
	private readonly CancellationTokenSource _closing = new();
	private readonly Lock _gate = new();
	private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private VncTransport? _transport;
	private VncRelay? _relay;
	private VncConnectionInfo _info;
	private int _disposed;

	/// <param name="credentials">A copy the session owns and disposes, used when it has to dial again.</param>
	public VncSession(VncConnector connector, VncTransport transport, LoginCredentials? credentials, ILogger logger)
	{
		ArgumentNullException.ThrowIfNull(transport);
		_connector = connector;
		_transport = transport;
		_credentials = credentials;
		_logger = logger;
		_info = Describe(transport);
	}

	public Task Completion => _completion.Task;

	public VncConnectionInfo Info
	{
		get
		{
			lock (_gate)
			{
				return _info;
			}
		}
	}

	public TFeature? GetFeature<TFeature>() where TFeature : class =>
		typeof(TFeature) == typeof(IVncConnection) ? (TFeature)(object)this : null;

	public async ValueTask<IVncChannel> AttachAsync(IVncSink sink, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(sink);
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

		// Closing the session cuts a dial short instead of waiting out its timeout or a certificate prompt.
		using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _closing.Token);
		await _attachLock.WaitAsync(linked.Token);
		try
		{
			// The session may have closed while this call waited for the lock, and a connection dialled now would
			// outlive it.
			ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

			// A second page replaces the first and takes its connection with it; a first one keeps the connection the
			// session was opened with.
			if (_relay is not null)
			{
				await ReleaseCurrentAsync();
			}

			VncTransport transport = _transport ?? await DialAsync(linked.Token);
			VncRelay relay = new(transport.Stream, transport.ServerInit.Raw, sink, _logger);
			_relay = relay;
			_ = WatchAsync(relay);
			return new AttachedChannel(this, relay);
		}
		finally
		{
			_attachLock.Release();
		}
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		await _closing.CancelAsync();
		await _attachLock.WaitAsync(CancellationToken.None);
		try
		{
			await ReleaseCurrentAsync();
		}
		finally
		{
			_attachLock.Release();
		}

		_credentials?.Dispose();
		_attachLock.Dispose();
		_closing.Dispose();
		_completion.TrySetResult();
	}

	private static VncConnectionInfo Describe(VncTransport transport) => new(
		transport.ServerInit.DesktopName,
		transport.ServerInit.Width,
		transport.ServerInit.Height,
		transport.IsEncrypted,
		VncSecurityTypes.Describe(transport.Security));

	private async Task<VncTransport> DialAsync(CancellationToken cancellationToken)
	{
		// Host names stay out of the log, which is a plain file.
		_logger.LogInformation("Opening the VNC connection again for a new view.");
		VncTransport transport = await _connector.ConnectAsync(_credentials, status: null, cancellationToken);
		lock (_gate)
		{
			_transport = transport;
			_info = Describe(transport);
		}

		return transport;
	}

	private async ValueTask DetachAsync(VncRelay relay)
	{
		// A view usually detaches because the session ended, which has already released everything, the lock included.
		if (Volatile.Read(ref _disposed) != 0)
		{
			return;
		}

		try
		{
			await _attachLock.WaitAsync(CancellationToken.None);
		}
		catch (ObjectDisposedException)
		{
			return;
		}

		try
		{
			if (!ReferenceEquals(_relay, relay))
			{
				return;
			}

			await ReleaseCurrentAsync();
		}
		finally
		{
			_attachLock.Release();
		}
	}

	/// <summary>
	/// Detaches the page and drops the connection with it. A page that has spoken to the server leaves RFB state
	/// (pixel format, encodings, a half sent update) that no other page can pick up, so the next attach dials again.
	/// </summary>
	private async ValueTask ReleaseCurrentAsync()
	{
		VncRelay? relay = _relay;
		_relay = null;
		if (relay is not null)
		{
			await relay.DisposeAsync();
		}

		VncTransport? transport;
		lock (_gate)
		{
			transport = _transport;
			_transport = null;
		}

		if (transport is not null)
		{
			await transport.DisposeAsync();
		}
	}

	private async Task WatchAsync(VncRelay relay)
	{
		try
		{
			await relay.Completion;
		}
		catch (Exception ex)
		{
			if (ReferenceEquals(_relay, relay))
			{
				_completion.TrySetException(ex);
			}

			return;
		}

		if (ReferenceEquals(_relay, relay))
		{
			_completion.TrySetResult();
		}
	}

	private sealed class AttachedChannel : IVncChannel
	{
		private readonly VncSession _session;
		private readonly VncRelay _relay;

		public AttachedChannel(VncSession session, VncRelay relay)
		{
			_session = session;
			_relay = relay;
		}

		public ValueTask StartAsync(CancellationToken cancellationToken = default) => _relay.StartAsync(cancellationToken);

		public ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default) =>
			_relay.SendAsync(data, cancellationToken);

		public ValueTask DisposeAsync() => _session.DetachAsync(_relay);
	}
}
