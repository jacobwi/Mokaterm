using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.Sessions.Terminal;

/// <summary>
/// One attached sink: a bounded queue of pooled output and a loop that delivers it. The queue bound is what
/// pushes back on the pump when the sink is slow. Disposing detaches the sink.
/// </summary>
internal sealed class TerminalSinkAttachment : IDisposable
{
	private readonly TerminalStream _owner;
	private readonly ITerminalSink _sink;
	private readonly ILogger _logger;
	private readonly Channel<OutputChunk> _queue;
	private readonly CancellationTokenSource _cancellation = new();
	private readonly Lock _closeLock = new();
	private bool _closed;

	public TerminalSinkAttachment(TerminalStream owner, ITerminalSink sink, int queueCapacity, ILogger logger)
	{
		_owner = owner;
		_sink = sink;
		_logger = logger;
		_queue = Channel.CreateBounded<OutputChunk>(new BoundedChannelOptions(queueCapacity)
		{
			FullMode = BoundedChannelFullMode.Wait,
			SingleReader = true,
		});
	}

	/// <summary>Queues output ahead of anything the pump sends. Call before the attachment is visible to publishers.</summary>
	public void EnqueueFirst(OutputChunk chunk)
	{
		if (!_queue.Writer.TryWrite(chunk))
		{
			chunk.Return();
		}
	}

	public void Start() => _ = Task.Run(DeliverAsync);

	/// <summary>Copies <paramref name="data"/> into the queue, waiting for space. Output for a detached sink is dropped.</summary>
	public async ValueTask EnqueueAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
	{
		OutputChunk chunk = OutputChunk.Copy(data.Span);
		try
		{
			await _queue.Writer.WriteAsync(chunk, cancellationToken);
		}
		catch (ChannelClosedException)
		{
			chunk.Return();
		}
		catch (OperationCanceledException)
		{
			chunk.Return();
			throw;
		}
	}

	public void Dispose() => _owner.Detach(this);

	/// <summary>Stops delivery and fails a publisher waiting for queue space. Safe to call more than once.</summary>
	internal void Close()
	{
		lock (_closeLock)
		{
			if (_closed)
			{
				return;
			}

			_closed = true;
			_ = _queue.Writer.TryComplete();
			_cancellation.Cancel();
		}
	}

	private async Task DeliverAsync()
	{
		ChannelReader<OutputChunk> reader = _queue.Reader;
		CancellationToken cancellationToken = _cancellation.Token;
		try
		{
			while (await reader.WaitToReadAsync(cancellationToken))
			{
				while (!cancellationToken.IsCancellationRequested && reader.TryRead(out OutputChunk chunk))
				{
					try
					{
						await _sink.WriteAsync(chunk.Memory, cancellationToken);
					}
					finally
					{
						// The sink may only use the memory during the call, so it goes back to the pool right away.
						chunk.Return();
					}
				}
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Terminal sink {SinkType} failed and was detached.", _sink.GetType().Name);
			_owner.Detach(this);
		}
		finally
		{
			// Closing first completes the writer, so nothing can be queued after the drain below.
			Close();
			while (reader.TryRead(out OutputChunk chunk))
			{
				chunk.Return();
			}

			_cancellation.Dispose();
		}
	}
}
