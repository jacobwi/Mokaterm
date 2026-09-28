using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Mokaterm.UI.Common.Interop;

namespace Mokaterm.Modules.Vnc.Interop;

/// <summary>Calls into vnc.js. Scoped, so the module is imported once per circuit or WebView.</summary>
internal sealed class VncInterop : IAsyncDisposable
{
	private const string ModulePath = "./_content/Mokaterm.Modules.Vnc/js/vnc.js";

	private readonly JsModule _module;

	public VncInterop(IJSRuntime jsRuntime) => _module = new JsModule(jsRuntime, ModulePath);

	/// <summary>Creates an RFB instance inside <paramref name="screen"/> and returns its id.</summary>
	public ValueTask<int> CreateAsync(
		ElementReference screen,
		DotNetObjectReference<VncJsCallbacks> callbacks,
		VncJsOptions options,
		CancellationToken cancellationToken) =>
		_module.InvokeAsync<int>("create", cancellationToken, screen, callbacks, options);

	/// <summary>Hands the page bytes from the server. Completes once noVNC has parsed them.</summary>
	public ValueTask DeliverAsync(int id, byte[] data, CancellationToken cancellationToken) =>
		_module.InvokeVoidAsync("deliver", cancellationToken, id, data);

	public ValueTask SetOptionsAsync(int id, IReadOnlyDictionary<string, object?> changes) =>
		_module.InvokeVoidAsync("setOptions", id, changes);

	public ValueTask FocusAsync(int id) => _module.InvokeVoidAsync("focus", id);

	/// <summary>Sends a key combination this machine would otherwise swallow, such as Ctrl+Alt+Del.</summary>
	public ValueTask SendComboAsync(int id, string combo) => _module.InvokeVoidAsync("sendCombo", id, combo);

	/// <summary>Pastes text into the session. False when the session takes no input.</summary>
	public ValueTask<bool> SendClipboardAsync(int id, string text) => _module.InvokeAsync<bool>("sendClipboard", id, text);

	/// <summary>Copies the text the session last sent onto this machine's clipboard. Returns its length, or -1 on failure.</summary>
	public ValueTask<int> ReceiveClipboardAsync(int id) => _module.InvokeAsync<int>("receiveClipboard", id);

	/// <summary>The screen as a PNG stream. The stream is empty when there is nothing to capture.</summary>
	public ValueTask<IJSStreamReference> ScreenshotAsync(int id, CancellationToken cancellationToken) =>
		_module.InvokeAsync<IJSStreamReference>("screenshot", cancellationToken, id);

	/// <summary>Enters or leaves full screen for <paramref name="root"/> and returns the new state.</summary>
	public ValueTask<bool> ToggleFullscreenAsync(int id, ElementReference root) =>
		_module.InvokeAsync<bool>("toggleFullscreen", id, root);

	/// <summary>Disposes the RFB instance. False when the circuit or WebView is already gone.</summary>
	public ValueTask<bool> TryDisposeInstanceAsync(int id) => _module.TryInvokeVoidAsync("dispose", id);

	public ValueTask DisposeAsync() => _module.DisposeAsync();
}
