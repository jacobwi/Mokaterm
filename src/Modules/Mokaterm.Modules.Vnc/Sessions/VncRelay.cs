using Microsoft.Extensions.Logging;
using Mokaterm.Modules.Vnc.Protocol;

namespace Mokaterm.Modules.Vnc.Sessions;

/// <summary>
/// Moves bytes between one page and one server connection. Server to page is a pump that waits for the page to take
/// each chunk, so a busy view slows the server down instead of filling memory; page to server is a plain write,
/// minus the handshake bytes the page side answers on its own.
/// </summary>
internal sealed class VncRelay : IVncChannel
{
	/// <summary>Large enough that a full screen update is a handful of interop calls, small enough to stay responsive.</summary>
	public const int ChunkSize = 64 * 1024;

	private readonly Stream _stream;
	private readonly IVncSink _sink;
	private readonly VncPageHandshake _handshake;
	private readonly ILogger _logger;
	private readonly SemaphoreSlim _sendLock = new(1, 1);
	private readonly CancellationTokenSource _stopping = new();
	private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private Task _pump = Task.CompletedTask;
	private int _disposed;

	/// <param name="stream">The session stream, past its handshake.</param>
	/// <param name="serverInit">The ServerInit bytes the server sent, replayed to the page.</param>
	public VncRelay(Stream stream, byte[] serverInit, IVncSink sink, ILogger logger)
	{
		_stream = stream;
		_sink = sink;
		_logger = logger;
		_handshake = new VncPageHandshake(serverInit);
	}

	public Task Completion => _completion.Task;

	/// <summary>Sends the synthetic greeting, which starts the page's own handshake.</summary>
	public ValueTask StartAsync(CancellationToken cancellationToken) =>
		_sink.WriteAsync(VncPageHandshake.Greeting(), cancellationToken);

	public async ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
	{
		if (Volatile.Read(ref _disposed) != 0 || data.IsEmpty)
		{
			return;
		}

		using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopping.Token);
		CancellationToken token = linked.Token;
		await _sendLock.WaitAsync(token);
		try
		{
			ReadOnlyMemory<byte> forward = data;
			if (!_handshake.IsComplete)
			{
				List<byte[]> replies = [];
				forward = _handshake.Consume(data, replies);
				foreach (byte[] reply in replies)
				{
					await _sink.WriteAsync(reply, token);
				}

				if (_handshake.IsComplete)
				{
					// Only now can the server's answers mean anything to the page.
					Volatile.Write(ref _pump, PumpAsync(_stopping.Token));
				}
			}

			if (!forward.IsEmpty)
			{
				await _stream.WriteAsync(forward, token);
				await _stream.FlushAsync(token);
			}
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_completion.TrySetException(Describe(ex));
			throw;
		}
		finally
		{
			_sendLock.Release();
		}
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		await _stopping.CancelAsync();
		try
		{
			await Volatile.Read(ref _pump);
		}
		catch (Exception ex)
		{
			_logger.LogDebug(ex, "The VNC relay pump ended with an error while detaching.");
		}

		_completion.TrySetResult();
		_stopping.Dispose();
		_sendLock.Dispose();
	}

	private static Exception Describe(Exception exception) => exception switch
	{
		VncProtocolException or IOException or ObjectDisposedException => new IOException(
			exception is VncProtocolException ? exception.Message : "The connection to the VNC server was lost.",
			exception),
		_ => exception,
	};

	private async Task PumpAsync(CancellationToken cancellationToken)
	{
		// Off the caller's context: the pump outlives the interop call that started it.
		await Task.Yield();
		byte[] buffer = new byte[ChunkSize];
		try
		{
			while (true)
			{
				int read = await _stream.ReadAsync(buffer, cancellationToken);
				if (read == 0)
				{
					_completion.TrySetResult();
					return;
				}

				await _sink.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			// Detached.
		}
		catch (Exception ex)
		{
			_logger.LogDebug(ex, "The VNC relay stopped reading from the server.");
			_completion.TrySetException(Describe(ex));
		}
	}
}
