using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Mokaterm.UI.Common.Interop;

namespace Mokaterm.UI.FileBrowser.Interop;

/// <summary>The browser-side helpers in <c>filebrowser.js</c>, shared by every browser and dialog in a UI scope.</summary>
internal sealed class FileBrowserInterop : IAsyncDisposable
{
	private const string ModulePath = "./_content/Mokaterm.UI.FileBrowser/js/filebrowser.js";

	private readonly JsModule _module;

	public FileBrowserInterop(IJSRuntime jsRuntime) => _module = new JsModule(jsRuntime, ModulePath);

	/// <summary>
	/// Wires keyboard shortcuts, dragging entries onto folders, and focus and scroll helpers to the browser root, and
	/// reports through <see cref="FileBrowserJsReceiver.OnWidthChanged"/> whenever the root's width crosses one of the
	/// ascending <paramref name="widthSteps"/>. The result exposes <c>setActive</c>, <c>scrollToRow</c>,
	/// <c>restoreFocus</c>, <c>focus</c> and <c>dispose</c>.
	/// </summary>
	public ValueTask<IJSObjectReference> AttachBrowserAsync(
		ElementReference root,
		DotNetObjectReference<FileBrowserJsReceiver> receiver,
		bool active,
		IReadOnlyList<int> widthSteps,
		CancellationToken cancellationToken) =>
		_module.InvokeAsync<IJSObjectReference>("attachBrowser", cancellationToken, root, receiver, active, widthSteps);

	/// <summary>
	/// Makes Enter press the dialog's <c>data-dialog-default</c> button, except on another focused button, and focuses its
	/// <c>data-dialog-focus</c> input, selecting the given range, or else the default button. Dispose the result to detach.
	/// </summary>
	public ValueTask<IJSObjectReference> BindDialogAsync(
		ElementReference root,
		int? selectionStart,
		int? selectionEnd,
		CancellationToken cancellationToken) =>
		_module.InvokeAsync<IJSObjectReference>("bindDialog", cancellationToken, root, selectionStart, selectionEnd);

	/// <summary>Scrolls the option marked <c>aria-selected</c> inside <paramref name="list"/> into view.</summary>
	public ValueTask<bool> ScrollSelectedIntoViewAsync(ElementReference list) =>
		_module.TryInvokeVoidAsync("scrollSelectedIntoView", list);

	public ValueTask DisposeAsync() => _module.DisposeAsync();
}
