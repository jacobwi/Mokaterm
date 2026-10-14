using Mokaterm.Modules.Rdp;

namespace Mokaterm.RdpProbe;

/// <summary>Stands in for the view: it only notes the first picture region the session sends.</summary>
internal sealed class FirstPictureSink : IRdpSink
{
	private readonly TaskCompletionSource<int> _firstFrame = new(TaskCreationOptions.RunContinuationsAsynchronously);

	/// <summary>Completes with the size of the first region's message.</summary>
	public Task<int> FirstFrame => _firstFrame.Task;

	public ValueTask FrameAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken)
	{
		_firstFrame.TrySetResult(frame.Length);
		return ValueTask.CompletedTask;
	}

	public ValueTask PointerImageAsync(RdpPointerImage cursor, CancellationToken cancellationToken) => ValueTask.CompletedTask;

	public ValueTask PointerStyleAsync(bool hidden, CancellationToken cancellationToken) => ValueTask.CompletedTask;

	public ValueTask PointerMovedAsync(int x, int y, CancellationToken cancellationToken) => ValueTask.CompletedTask;

	public ValueTask DesktopResizedAsync(int width, int height, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}
