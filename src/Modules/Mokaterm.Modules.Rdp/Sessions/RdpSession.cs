using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Rdp.Protocol;

namespace Mokaterm.Modules.Rdp.Sessions;

/// <summary>
/// An RDP session. The connection and the decoded desktop live here, not in the view, so a tab that is hidden or
/// a page that reloads costs nothing more than a repaint when it comes back.
/// </summary>
internal sealed class RdpSession : IProtocolSession, IRdpConnection
{
	private readonly RdpSessionLoop _loop;
	private readonly LoginCredentials? _credentials;
	private readonly RdpConnectionInfo _connected;
	private readonly SemaphoreSlim _attachLock = new(1, 1);
	private AttachedChannel? _channel;
	private int _disposed;

	/// <param name="credentials">A copy the session owns and disposes; nothing else keeps the login.</param>
	public RdpSession(RdpTransport transport, RdpSettings settings, LoginCredentials? credentials, ILogger logger)
	{
		ArgumentNullException.ThrowIfNull(transport);
		_connected = transport.Describe();
		_credentials = credentials;
		_loop = new RdpSessionLoop(transport, settings, logger);
		_loop.Start();
	}

	public Task Completion => _loop.Completion;

	public RdpConnectionInfo Info => _connected with { Width = _loop.Width, Height = _loop.Height };

	public TFeature? GetFeature<TFeature>() where TFeature : class =>
		typeof(TFeature) == typeof(IRdpConnection) ? (TFeature)(object)this : null;

	public async ValueTask<IRdpChannel> AttachAsync(IRdpSink sink, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(sink);
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

		await _attachLock.WaitAsync(cancellationToken);
		try
		{
			// A second page replaces the first, which stops being painted but leaves the session running.
			_channel?.MarkDetached();
			AttachedChannel channel = new(this);
			_channel = channel;
			_loop.Attach(sink);
			return channel;
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

		await _attachLock.WaitAsync(CancellationToken.None);
		try
		{
			_channel?.MarkDetached();
			_channel = null;
		}
		finally
		{
			_attachLock.Release();
		}

		await _loop.DisposeAsync();
		_credentials?.Dispose();
		_attachLock.Dispose();
	}

	private async ValueTask DetachAsync(AttachedChannel channel)
	{
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
			if (!ReferenceEquals(_channel, channel))
			{
				return;
			}

			_channel = null;
			_loop.Attach(null);
		}
		finally
		{
			_attachLock.Release();
		}
	}

	private sealed class AttachedChannel : IRdpChannel
	{
		private readonly RdpSession _session;
		private int _detached;

		public AttachedChannel(RdpSession session) => _session = session;

		private bool IsCurrent => Volatile.Read(ref _detached) == 0;

		/// <summary>Another page took over, so this channel stops working without the first one having to notice.</summary>
		public void MarkDetached() => Volatile.Write(ref _detached, 1);

		public ValueTask StartAsync(CancellationToken cancellationToken = default)
		{
			if (IsCurrent)
			{
				_session._loop.Repaint();
			}

			return ValueTask.CompletedTask;
		}

		public ValueTask SendInputAsync(IReadOnlyList<RdpInputEvent> events, CancellationToken cancellationToken = default) =>
			IsCurrent ? _session._loop.SendInputAsync(events, cancellationToken) : ValueTask.CompletedTask;

		public ValueTask SetViewOnlyAsync(bool viewOnly, CancellationToken cancellationToken = default) =>
			IsCurrent ? _session._loop.SetViewOnlyAsync(viewOnly, cancellationToken) : ValueTask.CompletedTask;

		public ValueTask<bool> ResizeAsync(int width, int height, CancellationToken cancellationToken = default) =>
			IsCurrent ? _session._loop.ResizeAsync(width, height, cancellationToken) : ValueTask.FromResult(false);

		public async ValueTask DisposeAsync()
		{
			if (Interlocked.Exchange(ref _detached, 1) != 0)
			{
				return;
			}

			await _session.DetachAsync(this);
		}
	}
}
