using System.Collections.Concurrent;
using System.IO.Pipelines;
using Mokaterm.Modules.Rdp.Protocol;
using Mokaterm.Modules.Rdp.Sessions;

namespace Mokaterm.Modules.Rdp.Tests.Fakes;

/// <summary>
/// A connection with nothing behind it. The test plays the server by sending whole messages, which the session reads
/// through the real <see cref="RdpFrameReader"/>, and sees everything the session wrote.
/// </summary>
internal sealed class FakeWire : IRdpWire, IDisposable
{
	private readonly Pipe _fromServer = new();
	private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private int _ended;

	public FakeWire() => Reader = new RdpFrameReader(_fromServer.Reader.AsStream());

	public RdpFrameReader Reader { get; }

	public ConcurrentQueue<byte[]> Sent { get; } = new();

	/// <summary>Completes once the session closed the connection.</summary>
	public Task Closed => _closed.Task;

	public ValueTask WriteAsync(byte[] bytes, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (Closed.IsCompleted)
		{
			throw new IOException("The connection is closed.");
		}

		Sent.Enqueue(bytes);
		return ValueTask.CompletedTask;
	}

	public ValueTask CloseAsync()
	{
		_closed.TrySetResult();
		End();
		return ValueTask.CompletedTask;
	}

	/// <summary>The server sends one message.</summary>
	public async Task SendAsync(byte[] message) =>
		await _fromServer.Writer.WriteAsync(message, TestContext.Current.CancellationToken).AsTask().WaitAsync(Eventually.Limit, TestContext.Current.CancellationToken);

	/// <summary>The server closes its end without a word, the way a dropped connection looks.</summary>
	public void HangUp() => End();

	public void Dispose() => Reader.Dispose();

	private void End()
	{
		if (Interlocked.Exchange(ref _ended, 1) == 0)
		{
			_fromServer.Writer.Complete();
		}
	}
}
