using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Globalization;
using Mokaterm.Modules.Rdp.Protocol;

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

	/// <summary>Every call in the order it came, as "resize 64x48", "frame 0,0 64x48", "pointer", "style hidden" or "moved 3,4".</summary>
	public ConcurrentQueue<string> Calls { get; } = new();

	/// <summary>Runs at the start of every call with the call's first word, so a test can make the page slow or broken.</summary>
	public Func<string, CancellationToken, ValueTask>? Before { get; set; }

	public async ValueTask FrameAsync(byte[] frame, CancellationToken cancellationToken)
	{
		await BeforeAsync("frame", cancellationToken);
		Frames.Enqueue(frame);
		Calls.Enqueue(string.Create(
			CultureInfo.InvariantCulture,
			$"frame {Field(frame, 1)},{Field(frame, 2)} {Field(frame, 3)}x{Field(frame, 4)}"));
		_arrived.Release();
	}

	public async ValueTask PointerImageAsync(RdpPointerImage cursor, CancellationToken cancellationToken)
	{
		await BeforeAsync("pointer", cancellationToken);
		Pointers.Enqueue(cursor);
		Calls.Enqueue("pointer");
	}

	public async ValueTask PointerStyleAsync(bool hidden, CancellationToken cancellationToken)
	{
		await BeforeAsync("style", cancellationToken);
		PointerStyles.Enqueue(hidden);
		Calls.Enqueue(hidden ? "style hidden" : "style default");
	}

	public async ValueTask PointerMovedAsync(int x, int y, CancellationToken cancellationToken)
	{
		await BeforeAsync("moved", cancellationToken);
		PointerMoves.Enqueue((x, y));
		Calls.Enqueue(string.Create(CultureInfo.InvariantCulture, $"moved {x},{y}"));
	}

	public async ValueTask DesktopResizedAsync(int width, int height, CancellationToken cancellationToken)
	{
		await BeforeAsync("resize", cancellationToken);
		Resizes.Enqueue((width, height));
		Calls.Enqueue(string.Create(CultureInfo.InvariantCulture, $"resize {width}x{height}"));
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

	/// <summary>One of the six 16 bit fields of <see cref="RdpFrameEncoder"/>'s header.</summary>
	public static int Field(byte[] frame, int index) =>
		frame.Length >= RdpFrameEncoder.HeaderLength ? BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(index * 2, 2)) : -1;

	private ValueTask BeforeAsync(string call, CancellationToken cancellationToken) =>
		Before is { } before ? before(call, cancellationToken) : ValueTask.CompletedTask;
}
