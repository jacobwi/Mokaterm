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

	/// <summary>
	/// For a list whose rows are tab stops marked <c>data-mt-row</c>: Space stays the row's own and the arrows, Home and
	/// End move the focus from row to row.
	/// </summary>
	public ValueTask<bool> AttachRowKeysAsync(ElementReference element) => _module.TryInvokeVoidAsync("attachRowKeys", element);

	/// <summary>
	/// Keeps the arrows, and Tab or Shift+Delete when the field's <c>data-mt-keys</c> names them, from moving the caret
	/// or the focus out of the combobox field inside <paramref name="root"/>.
	/// </summary>
	public ValueTask<bool> AttachComboboxKeysAsync(ElementReference root) => _module.TryInvokeVoidAsync("attachComboboxKeys", root);

	public ValueTask<bool> ScrollActiveIntoViewAsync(ElementReference element) =>
		_module.TryInvokeVoidAsync("scrollActiveIntoView", element);

	/// <summary>
	/// Scrolls the settings row with <paramref name="anchor"/> inside <paramref name="container"/> into view and makes it
	/// glow for a moment; no anchor scrolls back to the top of the page.
	/// </summary>
	public ValueTask<bool> RevealSettingAsync(ElementReference container, string? anchor) =>
		_module.TryInvokeVoidAsync("revealSetting", container, anchor);

	/// <summary>Where <paramref name="element"/> is in the viewport, or null when the page cannot say.</summary>
	public async ValueTask<ElementBox?> BoxOfAsync(ElementReference element)
	{
		try
		{
			return await _module.InvokeAsync<ElementBox?>("boxOf", element);
		}
		catch (Exception ex) when (JsModule.IsExpected(ex))
		{
			return null;
		}
	}

	public ValueTask DisposeAsync() => _module.DisposeAsync();
}
