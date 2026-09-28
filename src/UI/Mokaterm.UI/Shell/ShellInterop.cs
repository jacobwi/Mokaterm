using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Mokaterm.UI.Common.Interop;

namespace Mokaterm.UI.Shell;

/// <summary>
/// The shell's JS module: global shortcuts, idle reporting and list keys. Starting focus and Enter in forms live in
/// <see cref="FormInterop"/>. One import per scope, shared by every shell component.
/// </summary>
internal sealed class ShellInterop : IAsyncDisposable
{
	private const string ModulePath = "./_content/Mokaterm.UI/js/shell.js";

	private readonly JsModule _module;

	public ShellInterop(IJSRuntime jsRuntime) => _module = new JsModule(jsRuntime, ModulePath);

	/// <summary>Starts the window-level listeners. Returns a handle for <see cref="DetachShellAsync"/>.</summary>
	public ValueTask<int> AttachShellAsync<TCallbacks>(DotNetObjectReference<TCallbacks> callbacks, ShellListenerOptions options)
		where TCallbacks : class =>
		_module.InvokeAsync<int>("attachShell", callbacks, options);

	public ValueTask<bool> DetachShellAsync(int handle) => _module.TryInvokeVoidAsync("detachShell", handle);

	/// <summary>Whether leaving or reloading the page asks first. Only takes effect with <c>ConfirmLeave</c> set on attach.</summary>
	public ValueTask<bool> SetConfirmLeaveAsync(int handle, bool confirm) => _module.TryInvokeVoidAsync("setConfirmLeave", handle, confirm);

	/// <summary>Turns the global shortcuts off while the settings page records a new gesture.</summary>
	public ValueTask<bool> SetShortcutsEnabledAsync(int handle, bool enabled) =>
		_module.TryInvokeVoidAsync("setShortcutsEnabled", handle, enabled);

	/// <summary>Stops arrow keys and space from scrolling <paramref name="element"/> while it handles them itself.</summary>
	public ValueTask<bool> AttachListKeysAsync(ElementReference element) => _module.TryInvokeVoidAsync("attachListKeys", element);

	public ValueTask<bool> ScrollActiveIntoViewAsync(ElementReference element) =>
		_module.TryInvokeVoidAsync("scrollActiveIntoView", element);

	public ValueTask DisposeAsync() => _module.DisposeAsync();
}
