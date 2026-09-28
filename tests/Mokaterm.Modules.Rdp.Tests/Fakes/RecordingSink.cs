using System.Collections.Concurrent;

namespace Mokaterm.Modules.Rdp.Tests.Fakes;

/// <summary>Keeps everything a session sends to its page so a test can wait for it and look at it.</summary>
internal sealed class RecordingSink : IRdpSink, IDisposable
{
	private readonly SemaphoreSlim _arrived = new(0);

	public ConcurrentQueue<byte[]> Frames { get; } = new();

	public ConcurrentQueue<RdpPointerImage> Pointers { get; } = new();

	public ConcurrentQueue<bool> PointerStyles { get; } = new();

	public ConcurrentQueue<(int X, int Y)> PointerMoves { get; } = new();

	public ConcurrentQueue<(int Width, int Height)> Resizes { get; } = new();

	public ValueTask FrameAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken)
	{
		Frames.Enqueue(frame.ToArray());
		_arrived.Release();
		return ValueTask.CompletedTask;
	}

	public ValueTask PointerImageAsync(RdpPointerImage cursor, CancellationToken cancellationToken)
	{
		Pointers.Enqueue(cursor);
		return ValueTask.CompletedTask;
	}

	public ValueTask PointerStyleAsync(bool hidden, CancellationToken cancellationToken)
	{
		PointerStyles.Enqueue(hidden);
		return ValueTask.CompletedTask;
	}

	public ValueTask PointerMovedAsync(int x, int y, CancellationToken cancellationToken)
	{
		PointerMoves.Enqueue((x, y));
		return ValueTask.CompletedTask;
	}

	public ValueTask DesktopResizedAsync(int width, int height, CancellationToken cancellationToken)
	{
		Resizes.Enqueue((width, height));
		return ValueTask.CompletedTask;
	}

	/// <summary>Waits until at least <paramref name="count"/> frames have arrived.</summary>
	public async Task WaitForFramesAsync(int count, CancellationToken cancellationToken)
	{
		while (Frames.Count < count)
		{
			await _arrived.WaitAsync(cancellationToken);
		}
	}

	public void Dispose() => _arrived.Dispose();
}
