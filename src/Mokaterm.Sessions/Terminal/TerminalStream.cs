using System.Buffers;
using System.Text;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.Sessions.Terminal;

/// <summary>
/// Pumps a session's terminal channel into a replay buffer and every attached sink. The stream lives as long as
/// the session handle: a reconnect binds the new channel to the same stream and attached views carry on.
/// </summary>
internal sealed class TerminalStream : ITerminalStream, IAsyncDisposable
{
	internal const int ReadBufferSize = 32 * 1024;

	/// <summary>Chunks queued per sink before the pump waits. Bounds memory to about this many read buffers per view.</summary>
	internal const int SinkQueueCapacity = 8;

	private readonly ILogger _logger;
	private readonly ReplayBuffer _replay;
	private readonly Lock _lock = new();

	// Publishing is serialized so every sink sees chunks in read order and each chunk lands either in the replay
	// snapshot a new sink starts with or in its queue, never both.
	private readonly SemaphoreSlim _publishGate = new(1, 1);

	// Input and resizes share one gate so a resize never overtakes keystrokes typed before it.
	private readonly SemaphoreSlim _writeGate = new(1, 1);

	private TerminalSinkAttachment[] _attachments = [];
	private ITerminalChannel? _channel;
	private CancellationTokenSource? _pumpCancellation;
	private TerminalSize _size;
	private TerminalSize _remoteSize;
	private int _generation;
	private bool _isOpen;
	private bool _disposed;

	public TerminalStream(int replayBufferBytes, ILogger logger)
	{
		_replay = new ReplayBuffer(replayBufferBytes);
		_logger = logger;
	}

	public event Action? StateChanged;

	/// <summary>The latest size a view asked for. It is sent to the channel when open, or to the next one bound.</summary>
	public TerminalSize Size
	{
		get
		{
			lock (_lock)
			{
				return _size;
			}
		}
	}

	public bool IsOpen
	{
		get
		{
			lock (_lock)
			{
				return _isOpen;
			}
		}
	}

	/// <summary>
	/// Starts pumping <paramref name="channel"/>, which the remote side opened at <paramref name="openedSize"/>. The
	/// session owns the channel; the stream never disposes it. Returns a task that completes when the pump stops.
	/// </summary>
	/// <param name="generation">
	/// The connect attempt the channel belongs to. A channel from an attempt older than the one already bound is ignored:
	/// a connect that finished late must not take the stream away from the connection that replaced it.
	/// </param>
	public Task Bind(ITerminalChannel channel, TerminalSize openedSize, int generation)
	{
		ArgumentNullException.ThrowIfNull(channel);
		CancellationTokenSource pumpCancellation = new();
		CancellationToken cancellationToken = pumpCancellation.Token;
		CancellationTokenSource? previous;
		bool wasOpen;
		lock (_lock)
		{
			if (_disposed || generation < _generation)
			{
				pumpCancellation.Dispose();
				return Task.CompletedTask;
			}

			_generation = generation;
			previous = _pumpCancellation;
			_pumpCancellation = pumpCancellation;
			_channel = channel;
			_remoteSize = openedSize;
			if (!_size.IsValid)
			{
				_size = openedSize;
			}

			wasOpen = _isOpen;
			_isOpen = true;
		}

		if (previous is not null)
		{
			previous.Cancel();
			previous.Dispose();
		}

		Task pump = Task.Run(() => PumpAsync(channel, pumpCancellation, cancellationToken));
		if (!wasOpen)
		{
			EventRaiser.Raise(StateChanged, _logger, nameof(StateChanged));
		}

		return pump;
	}

	public IDisposable Attach(ITerminalSink sink)
	{
		ArgumentNullException.ThrowIfNull(sink);
		TerminalSinkAttachment attachment = new(this, sink, SinkQueueCapacity, _logger);
		lock (_lock)
		{
			if (_disposed)
			{
				// A view mounting while its tab closes gets a detached attachment instead of an exception.
				attachment.Close();
			}
			else
			{
				if (_replay.Snapshot() is { } replay)
				{
					attachment.EnqueueFirst(replay);
				}

				_attachments = [.. _attachments, attachment];
			}
		}

		attachment.Start();
		return attachment;
	}

	public async ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
	{
		if (data.IsEmpty)
		{
			return;
		}

		await _writeGate.WaitAsync(cancellationToken);
		try
		{
			ITerminalChannel? channel = GetOpenChannel();
			if (channel is not null)
			{
				await channel.WriteAsync(data, cancellationToken);
			}
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			// The channel is going away; input typed at that moment is dropped like input sent while closed.
			_logger.LogDebug(ex, "Dropped terminal input because the channel failed.");
		}
		finally
		{
			_writeGate.Release();
		}
	}

	public async ValueTask SendTextAsync(string text, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(text);
		if (text.Length == 0)
		{
			return;
		}

		byte[] buffer = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetMaxByteCount(text.Length));
		try
		{
			int length = Encoding.UTF8.GetBytes(text, buffer);
			await SendAsync(buffer.AsMemory(0, length), cancellationToken);
		}
		finally
		{
			// Typed text can be a password answering a prompt.
			ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
		}
	}

	public async ValueTask ResizeAsync(TerminalSize size, CancellationToken cancellationToken = default)
	{
		if (!size.IsValid)
		{
			return;
		}

		await _writeGate.WaitAsync(cancellationToken);
		try
		{
			ITerminalChannel? channel;
			lock (_lock)
			{
				_size = size;
				channel = _isOpen && size != _remoteSize ? _channel : null;
			}

			if (channel is not null)
			{
				await SendResizeAsync(channel, size, cancellationToken);
			}
		}
		finally
		{
			_writeGate.Release();
		}
	}

	public async ValueTask WriteLocalAsync(string text, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(text);
		if (text.Length == 0)
		{
			return;
		}

		string normalized = ToCrLf(text);
		byte[] buffer = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetMaxByteCount(normalized.Length));
		try
		{
			int length = Encoding.UTF8.GetBytes(normalized, buffer);
			await PublishAsync(buffer.AsMemory(0, length), cancellationToken);
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(buffer);
		}
	}

	public ValueTask DisposeAsync()
	{
		TerminalSinkAttachment[] attachments;
		CancellationTokenSource? pumpCancellation;
		bool wasOpen;
		lock (_lock)
		{
			if (_disposed)
			{
				return ValueTask.CompletedTask;
			}

			_disposed = true;
			attachments = _attachments;
			_attachments = [];
			pumpCancellation = _pumpCancellation;
			_pumpCancellation = null;
			_channel = null;
			wasOpen = _isOpen;
			_isOpen = false;
		}

		// Closing the attachments releases a pump waiting for queue space; cancelling stops its next read.
		foreach (TerminalSinkAttachment attachment in attachments)
		{
			attachment.Close();
		}

		if (pumpCancellation is not null)
		{
			pumpCancellation.Cancel();
			pumpCancellation.Dispose();
		}

		if (wasOpen)
		{
			EventRaiser.Raise(StateChanged, _logger, nameof(StateChanged));
		}

		return ValueTask.CompletedTask;
	}

	internal void Detach(TerminalSinkAttachment attachment)
	{
		lock (_lock)
		{
			if (Array.IndexOf(_attachments, attachment) >= 0)
			{
				_attachments = [.. _attachments.Where(existing => !ReferenceEquals(existing, attachment))];
			}
		}

		attachment.Close();
	}

	/// <summary>Converts lone line feeds to CRLF so local text starts each line at column 0.</summary>
	private static string ToCrLf(string text)
	{
		int bare = 0;
		for (int i = 0; i < text.Length; i++)
		{
			if (text[i] == '\n' && (i == 0 || text[i - 1] != '\r'))
			{
				bare++;
			}
		}

		return bare == 0
			? text
			: string.Create(text.Length + bare, text, static (span, source) =>
			{
				int position = 0;
				for (int i = 0; i < source.Length; i++)
				{
					if (source[i] == '\n' && (i == 0 || source[i - 1] != '\r'))
					{
						span[position++] = '\r';
					}

					span[position++] = source[i];
				}
			});
	}

	private ITerminalChannel? GetOpenChannel()
	{
		lock (_lock)
		{
			return _isOpen ? _channel : null;
		}
	}

	private async Task PumpAsync(ITerminalChannel channel, CancellationTokenSource pumpCancellation, CancellationToken cancellationToken)
	{
		byte[] buffer = ArrayPool<byte>.Shared.Rent(ReadBufferSize);
		try
		{
			await SyncRemoteSizeAsync(channel, pumpCancellation, cancellationToken);
			while (true)
			{
				int read = await channel.ReadAsync(buffer.AsMemory(0, ReadBufferSize), cancellationToken);
				if (read <= 0)
				{
					break;
				}

				await PublishAsync(buffer.AsMemory(0, read), cancellationToken);
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception ex)
		{
			_logger.LogDebug(ex, "The terminal channel failed while reading.");
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(buffer);
		}

		bool closed = false;
		lock (_lock)
		{
			// A rebind or dispose replaced this pump; the stream's state belongs to the newer owner.
			if (ReferenceEquals(_pumpCancellation, pumpCancellation))
			{
				_isOpen = false;
				_channel = null;
				closed = true;
			}
		}

		if (closed)
		{
			EventRaiser.Raise(StateChanged, _logger, nameof(StateChanged));
		}
	}

	/// <summary>Sends the size a view reported while no channel was bound, before the first read.</summary>
	private async Task SyncRemoteSizeAsync(ITerminalChannel channel, CancellationTokenSource pumpCancellation, CancellationToken cancellationToken)
	{
		await _writeGate.WaitAsync(cancellationToken);
		try
		{
			TerminalSize size;
			lock (_lock)
			{
				if (!ReferenceEquals(_pumpCancellation, pumpCancellation) || !_size.IsValid || _size == _remoteSize)
				{
					return;
				}

				size = _size;
			}

			await SendResizeAsync(channel, size, cancellationToken);
		}
		finally
		{
			_writeGate.Release();
		}
	}

	/// <summary>Sends a size and records it as the remote size. Call while holding the write gate.</summary>
	private async Task SendResizeAsync(ITerminalChannel channel, TerminalSize size, CancellationToken cancellationToken)
	{
		try
		{
			await channel.ResizeAsync(size, cancellationToken);
			lock (_lock)
			{
				if (ReferenceEquals(_channel, channel))
				{
					_remoteSize = size;
				}
			}
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.LogDebug(ex, "Could not resize the remote terminal.");
		}
	}

	private async ValueTask PublishAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
	{
		await _publishGate.WaitAsync(cancellationToken);
		try
		{
			TerminalSinkAttachment[] targets;
			lock (_lock)
			{
				if (_disposed)
				{
					return;
				}

				_replay.Append(data.Span);
				targets = _attachments;
			}

			foreach (TerminalSinkAttachment target in targets)
			{
				await target.EnqueueAsync(data, cancellationToken);
			}
		}
		finally
		{
			_publishGate.Release();
		}
	}
}
