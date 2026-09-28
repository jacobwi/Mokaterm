using Mokaterm.UI.Common.Interop;

namespace Mokaterm.Modules.Rdp.Interop;

/// <summary>
/// Feeds one screen in the page. Every call completes only after the page has taken the work, which is what makes
/// a busy or far away view slow the frame rate down instead of piling frames up in memory.
/// </summary>
internal sealed class RdpPageSink : IRdpSink, IDisposable
{
	private readonly RdpInterop _interop;
	private readonly int _instanceId;
	private readonly Action<int, int>? _onDesktopResized;
	private readonly PageCalls _calls;

	/// <param name="dispatch">Runs work on the renderer's dispatcher.</param>
	/// <param name="onDesktopResized">Told about a new desktop size once the page has it, on the dispatcher.</param>
	public RdpPageSink(RdpInterop interop, int instanceId, Func<Func<Task>, Task> dispatch, Action<int, int>? onDesktopResized = null)
	{
		_interop = interop;
		_instanceId = instanceId;
		_onDesktopResized = onDesktopResized;
		_calls = new PageCalls(dispatch);
	}

	public ValueTask FrameAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken)
	{
		if (frame.IsEmpty)
		{
			return ValueTask.CompletedTask;
		}

		// The encoder reuses its buffer, so the frame is copied before it crosses into JavaScript.
		byte[] copy = frame.ToArray();
		return _calls.CallAsync(token => _interop.FrameAsync(_instanceId, copy, token), cancellationToken);
	}

	public ValueTask PointerImageAsync(RdpPointerImage cursor, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(cursor);
		return _calls.CallAsync(
			token => _interop.PointerImageAsync(_instanceId, cursor.Png, cursor.HotspotX, cursor.HotspotY, token),
			cancellationToken);
	}

	public ValueTask PointerStyleAsync(bool hidden, CancellationToken cancellationToken) =>
		_calls.CallAsync(token => _interop.PointerStyleAsync(_instanceId, hidden, token), cancellationToken);

	public ValueTask PointerMovedAsync(int x, int y, CancellationToken cancellationToken) =>
		_calls.CallAsync(token => _interop.PointerMovedAsync(_instanceId, x, y, token), cancellationToken);

	public ValueTask DesktopResizedAsync(int width, int height, CancellationToken cancellationToken) =>
		_calls.CallAsync(
			async token =>
			{
				await _interop.ResizeAsync(_instanceId, width, height, token);
				_onDesktopResized?.Invoke(width, height);
			},
			cancellationToken);

	/// <summary>Releases a call still waiting on the page, for example when the view goes away.</summary>
	public void Dispose() => _calls.Dispose();
}
