using Microsoft.JSInterop;
using Mokaterm.Modules.Rdp.Components;

namespace Mokaterm.Modules.Rdp.Interop;

/// <summary>
/// What rdp.js calls back into. A separate object so the public <see cref="RdpSessionView"/> does not gain
/// JS-only public methods.
/// </summary>
internal sealed class RdpJsCallbacks
{
	private readonly RdpSessionView _view;

	public RdpJsCallbacks(RdpSessionView view) => _view = view;

	[JSInvokable]
	public Task OnInput(RdpInputEvent[] events) => _view.HandleInputAsync(events);

	/// <summary>The element holding the screen has a new size in device pixels.</summary>
	[JSInvokable]
	public Task OnViewSize(int width, int height) => _view.HandleViewSizeAsync(width, height);

	[JSInvokable]
	public void OnFullscreen(bool fullscreen) => _view.HandleFullscreen(fullscreen);
}
