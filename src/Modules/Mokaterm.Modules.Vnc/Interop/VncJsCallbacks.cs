using Microsoft.JSInterop;
using Mokaterm.Modules.Vnc.Components;

namespace Mokaterm.Modules.Vnc.Interop;

/// <summary>
/// What vnc.js calls back into. A separate object so the public <see cref="VncSessionView"/> does not gain
/// JS-only public methods.
/// </summary>
internal sealed class VncJsCallbacks
{
	private readonly VncSessionView _view;

	public VncJsCallbacks(VncSessionView view) => _view = view;

	[JSInvokable]
	public Task OnData(byte[] data) => _view.HandleDataAsync(data);

	[JSInvokable]
	public void OnOpened() => _view.HandleOpened();

	[JSInvokable]
	public void OnClosed(bool clean) => _view.HandleClosed(clean);

	[JSInvokable]
	public void OnDesktopName(string name) => _view.HandleDesktopName(name);

	[JSInvokable]
	public void OnScreenSize(int width, int height) => _view.HandleScreenSize(width, height);

	[JSInvokable]
	public void OnClipboard(int length) => _view.HandleClipboard(length);

	[JSInvokable]
	public void OnClipboardRefused(int length) => _view.HandleClipboardRefused(length);

	[JSInvokable]
	public void OnFullscreen(bool fullscreen) => _view.HandleFullscreen(fullscreen);
}
