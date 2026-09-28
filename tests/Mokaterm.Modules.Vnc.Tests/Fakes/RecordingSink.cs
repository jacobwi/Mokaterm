using System.Collections.Concurrent;

namespace Mokaterm.Modules.Vnc.Tests.Fakes;

/// <summary>
/// Stands in for the page: records every chunk the relay delivers and can hold a write open, which is how the
/// backpressure test proves the relay stops reading while the view is busy.
/// </summary>
internal sealed class RecordingSink : IVncSink, IDisposable
{
	private readonly SemaphoreSlim _written = new(0);
	private TaskCompletionSource? _block;

	public ConcurrentQueue<byte[]> Chunks { get; } = new();

	public int WriteCount { get; private set; }

	/// <summary>All chunks so far, joined.</summary>
	public byte[] Bytes => [.. Chunks.SelectMany(chunk => chunk)];

	/// <summary>Makes the next writes wait until <see cref="Release"/>.</summary>
	public void Block() => _block = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

	public void Release() => Interlocked.Exchange(ref _block, null)?.TrySetResult();

	public async ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
	{
		if (_block is { } block)
		{
			await block.Task.WaitAsync(cancellationToken);
		}

		// The relay reuses its buffer, so a recorded chunk has to be a copy.
		Chunks.Enqueue(data.ToArray());
		WriteCount++;
		_written.Release();
	}

	/// <summary>Waits until at least <paramref name="count"/> chunks arrived.</summary>
	public async Task WaitForChunksAsync(int count, CancellationToken cancellationToken)
	{
		while (Chunks.Count < count)
		{
			await _written.WaitAsync(cancellationToken);
		}
	}

	/// <summary>Waits until the recorded bytes reach <paramref name="length"/>.</summary>
	public async Task WaitForBytesAsync(int length, CancellationToken cancellationToken)
	{
		while (Chunks.Sum(chunk => chunk.Length) < length)
		{
			await _written.WaitAsync(cancellationToken);
		}
	}

	public void Dispose()
	{
		Release();
		_written.Dispose();
	}
}
