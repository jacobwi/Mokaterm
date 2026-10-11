using System.Threading.Channels;
using Renci.SshNet.Messages.Connection;

namespace Mokaterm.Modules.Ssh.Forwarding;

/// <summary>
/// Our half of one channel the server opened, an X11 or agent connection. SSH.NET keeps its channel classes internal,
/// so this speaks the channel messages itself (RFC 4254 section 5): data both ways within each side's window, then EOF
/// and CLOSE. Unlike SSH.NET's own channels it reopens its window only as data is read, so a reader that falls behind
/// holds the server back instead of filling memory: what waits here never exceeds <see cref="WindowSize"/>.
/// </summary>
/// <remarks>
/// The <c>On</c> methods run on SSH.NET's message loop and never send or block there; replies go out from the caller of
/// <see cref="ReadAsync"/>, <see cref="WriteAsync"/> and <see cref="Close"/>, or from the thread pool.
/// </remarks>
internal sealed class SshForwardedChannel
{
	private readonly ISshMessageSender _sender;
	private readonly int _maxChunk;
	private readonly Channel<byte[]> _inbound = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });
	private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

	// State the message loop changes. Never held while sending.
	private readonly Lock _lock = new();

	// Orders what this side sends, so no data can follow EOF or CLOSE; OpenSSH drops a connection whose peer does that.
	private readonly Lock _sendLock = new();

	private byte[]? _pending;
	private int _pendingOffset;
	private long _remoteWindow;
	private long _localWindow;
	private long _unacknowledged;
	private TaskCompletionSource? _windowOpened;
	private bool _remoteClosed;
	private bool _eofSent;
	private bool _closeSent;

	/// <param name="remoteWindow">How much the server said it takes before it opens its window again.</param>
	/// <param name="remoteMaxPacket">The most data the server takes in one message.</param>
	/// <param name="windowSize">How much this side lets the server send ahead of the reader.</param>
	/// <param name="maxPacket">The most data this side takes in one message, and sends in one.</param>
	public SshForwardedChannel(ISshMessageSender sender, uint localNumber, uint remoteNumber, uint remoteWindow, uint remoteMaxPacket, uint windowSize, uint maxPacket)
	{
		ArgumentOutOfRangeException.ThrowIfZero(windowSize);
		ArgumentOutOfRangeException.ThrowIfZero(maxPacket);
		_sender = sender;
		LocalNumber = localNumber;
		RemoteNumber = remoteNumber;
		WindowSize = windowSize;
		MaxPacket = maxPacket;
		_remoteWindow = remoteWindow;
		_localWindow = windowSize;
		_maxChunk = (int)Math.Clamp(remoteMaxPacket, 1u, maxPacket);
	}

	/// <summary>This side's number for the channel, which the server addresses its messages to.</summary>
	public uint LocalNumber { get; }

	/// <summary>The server's number for the channel, which every message from this side is addressed to.</summary>
	public uint RemoteNumber { get; }

	public uint WindowSize { get; }

	public uint MaxPacket { get; }

	/// <summary>
	/// Completes once the channel is over: the server closed it, this side did, the server broke its window or the
	/// connection went away. Whatever relays the channel stops then.
	/// </summary>
	public Task Closed => _closed.Task;

	/// <summary>Accepts the channel. False when the connection is already gone.</summary>
	public bool Confirm() =>
		_sender.TrySend(new ChannelOpenConfirmationMessage(RemoteNumber, WindowSize, MaxPacket, LocalNumber));

	/// <summary>Takes data the server sent, in order. 0 once the server sent EOF or the channel is over.</summary>
	public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
	{
		if (buffer.IsEmpty)
		{
			return 0;
		}

		while (_pending is null)
		{
			if (_inbound.Reader.TryRead(out byte[]? chunk))
			{
				_pending = chunk;
				_pendingOffset = 0;
			}
			else if (!await _inbound.Reader.WaitToReadAsync(cancellationToken))
			{
				return 0;
			}
		}

		int count = Math.Min(buffer.Length, _pending.Length - _pendingOffset);
		_pending.AsSpan(_pendingOffset, count).CopyTo(buffer.Span);
		_pendingOffset += count;
		if (_pendingOffset == _pending.Length)
		{
			_pending = null;
		}

		Acknowledge(count);
		return count;
	}

	/// <summary>
	/// Sends <paramref name="data"/>, waiting while the server's window is shut. False once this side sent EOF or CLOSE,
	/// the server closed the channel or the connection went away; nothing is sent after that.
	/// </summary>
	public async ValueTask<bool> WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
	{
		int offset = 0;
		while (offset < data.Length)
		{
			int chunk = 0;
			Task? opened = null;
			lock (_lock)
			{
				if (_remoteClosed || _closed.Task.IsCompleted)
				{
					return false;
				}

				if (_remoteWindow <= 0)
				{
					_windowOpened ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
					opened = _windowOpened.Task;
				}
				else
				{
					chunk = (int)Math.Min(Math.Min(data.Length - offset, _remoteWindow), _maxChunk);
					_remoteWindow -= chunk;
				}
			}

			if (opened is not null)
			{
				await opened.WaitAsync(cancellationToken);
				continue;
			}

			byte[] payload = data.Slice(offset, chunk).ToArray();
			lock (_sendLock)
			{
				if (_eofSent || _closeSent || !_sender.TrySend(new ChannelDataMessage(RemoteNumber, payload)))
				{
					return false;
				}
			}

			offset += chunk;
		}

		return true;
	}

	/// <summary>Tells the server nothing more is coming from this side. The server may still send.</summary>
	public void SendEof()
	{
		lock (_sendLock)
		{
			if (_eofSent || _closeSent)
			{
				return;
			}

			_eofSent = true;
			_ = _sender.TrySend(new ChannelEofMessage(RemoteNumber));
		}
	}

	/// <summary>Closes this side once: EOF if it was not sent, then CLOSE. Reads end and writes stop.</summary>
	public void Close()
	{
		lock (_sendLock)
		{
			if (!_closeSent)
			{
				_closeSent = true;
				if (!_eofSent)
				{
					_eofSent = true;
					_ = _sender.TrySend(new ChannelEofMessage(RemoteNumber));
				}

				_ = _sender.TrySend(new ChannelCloseMessage(RemoteNumber));
			}
		}

		End();
	}

	/// <summary>Data from the server. More than the window allows ends the channel, since buffering it is how memory runs out.</summary>
	public void OnData(ReadOnlySpan<byte> data)
	{
		if (data.IsEmpty)
		{
			return;
		}

		bool overflow;
		lock (_lock)
		{
			if (_remoteClosed || _closed.Task.IsCompleted)
			{
				return;
			}

			overflow = data.Length > _localWindow;
			if (!overflow)
			{
				_localWindow -= data.Length;
				_ = _inbound.Writer.TryWrite(data.ToArray());
			}
		}

		if (overflow)
		{
			End();
		}
	}

	public void OnWindowAdjust(uint bytesToAdd)
	{
		TaskCompletionSource? opened;
		lock (_lock)
		{
			// RFC 4254 caps a window at 2^32 - 1, so a server that adds past that cannot make this side send more.
			_remoteWindow = Math.Min(_remoteWindow + bytesToAdd, uint.MaxValue);
			opened = _windowOpened;
			_windowOpened = null;
		}

		_ = opened?.TrySetResult();
	}

	/// <summary>The server sends no more data; whatever arrived is still read.</summary>
	public void OnEof() => _inbound.Writer.TryComplete();

	public void OnClose()
	{
		lock (_lock)
		{
			_remoteClosed = true;
		}

		End();
	}

	/// <summary>A channel request from the server. These channels take none, so one that wants an answer gets a failure.</summary>
	public void OnRequest(bool wantReply)
	{
		if (!wantReply)
		{
			return;
		}

		// Off the message loop: the reply waits for the send lock, which a writer may hold.
		_ = Task.Run(() =>
		{
			lock (_sendLock)
			{
				if (!_closeSent)
				{
					_ = _sender.TrySend(new ChannelFailureMessage(RemoteNumber));
				}
			}
		});
	}

	/// <summary>The connection went away: nothing can be sent any more, so the channel just ends.</summary>
	public void OnConnectionLost()
	{
		lock (_sendLock)
		{
			_eofSent = true;
			_closeSent = true;
		}

		End();
	}

	private void Acknowledge(int count)
	{
		long adjust = 0;
		lock (_lock)
		{
			_unacknowledged += count;
			if (_unacknowledged >= WindowSize / 2 && !_remoteClosed && !_closed.Task.IsCompleted)
			{
				adjust = _unacknowledged;
				_unacknowledged = 0;
				_localWindow += adjust;
			}
		}

		if (adjust > 0)
		{
			lock (_sendLock)
			{
				if (!_closeSent)
				{
					_ = _sender.TrySend(new ChannelWindowAdjustMessage(RemoteNumber, (uint)adjust));
				}
			}
		}
	}

	private void End()
	{
		TaskCompletionSource? opened;
		lock (_lock)
		{
			opened = _windowOpened;
			_windowOpened = null;
		}

		_ = _inbound.Writer.TryComplete();
		_ = opened?.TrySetResult();
		_ = _closed.TrySetResult();
	}
}
