using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Mokaterm.UI.Common.Interop;

namespace Mokaterm.Modules.Rdp.Interop;

/// <summary>Calls into rdp.js. Scoped, so the module is imported once per circuit or WebView.</summary>
internal sealed class RdpInterop : IAsyncDisposable
{
	private const string ModulePath = "./_content/Mokaterm.Modules.Rdp/js/rdp.js";

	private readonly JsModule _module;

	public RdpInterop(IJSRuntime jsRuntime) => _module = new JsModule(jsRuntime, ModulePath);

	/// <summary>Creates a screen inside <paramref name="screen"/> and returns its id.</summary>
	public ValueTask<int> CreateAsync(
		ElementReference screen,
		DotNetObjectReference<RdpJsCallbacks> callbacks,
		RdpJsOptions options,
		CancellationToken cancellationToken) =>
		_module.InvokeAsync<int>("create", cancellationToken, screen, callbacks, options);

	/// <summary>Paints one region. The frame carries its own position, size and format.</summary>
	public ValueTask FrameAsync(int id, byte[] frame, CancellationToken cancellationToken) =>
		_module.InvokeVoidAsync("frame", cancellationToken, id, frame);

	/// <summary>Sets the cursor to a picture the server drew.</summary>
	public ValueTask PointerImageAsync(int id, byte[] png, int hotspotX, int hotspotY, CancellationToken cancellationToken) =>
		_module.InvokeVoidAsync("pointerImage", cancellationToken, id, png, hotspotX, hotspotY);

	/// <summary>Hides the cursor or puts the default one back.</summary>
	public ValueTask PointerStyleAsync(int id, bool hidden, CancellationToken cancellationToken) =>
		_module.InvokeVoidAsync("pointerStyle", cancellationToken, id, hidden);

	/// <summary>The server moved the pointer, so the page stops predicting where it is.</summary>
	public ValueTask PointerMovedAsync(int id, int x, int y, CancellationToken cancellationToken) =>
		_module.InvokeVoidAsync("pointerMoved", cancellationToken, id, x, y);

	/// <summary>The desktop has a new size: the canvas is rebuilt and the old picture dropped.</summary>
	public ValueTask ResizeAsync(int id, int width, int height, CancellationToken cancellationToken) =>
		_module.InvokeVoidAsync("resize", cancellationToken, id, width, height);

	public ValueTask SetOptionsAsync(int id, IReadOnlyDictionary<string, object?> changes) =>
		_module.InvokeVoidAsync("setOptions", id, changes);

	public ValueTask FocusAsync(int id) => _module.InvokeVoidAsync("focus", id);

	/// <summary>The screen as a PNG stream. The stream is empty when there is nothing to capture.</summary>
	public ValueTask<IJSStreamReference> ScreenshotAsync(int id, CancellationToken cancellationToken) =>
		_module.InvokeAsync<IJSStreamReference>("screenshot", cancellationToken, id);

	/// <summary>Enters or leaves full screen for <paramref name="root"/> and returns the new state.</summary>
	public ValueTask<bool> ToggleFullscreenAsync(int id, ElementReference root) =>
		_module.InvokeAsync<bool>("toggleFullscreen", id, root);

	/// <summary>Disposes the screen. False when the circuit or WebView is already gone.</summary>
	public ValueTask<bool> TryDisposeInstanceAsync(int id) => _module.TryInvokeVoidAsync("dispose", id);

	public ValueTask DisposeAsync() => _module.DisposeAsync();
}
