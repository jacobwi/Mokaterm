using Microsoft.JSInterop;
using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.UI.Terminal.Interop;

/// <summary>
/// What terminal.js calls back into. A separate object so the public <see cref="TerminalView"/> does not gain
/// JS-only public methods.
/// </summary>
internal sealed class TerminalJsCallbacks
{
	private readonly TerminalView _view;

	public TerminalJsCallbacks(TerminalView view) => _view = view;

	[JSInvokable]
	public Task OnInput(string data) => _view.HandleInputAsync(data);

	[JSInvokable]
	public Task OnBinaryInput(byte[] data) => _view.HandleBinaryInputAsync(data);

	[JSInvokable]
	public Task OnResize(int cols, int rows, int width, int height) =>
		_view.HandleResizeAsync(new TerminalSize(cols, rows, width, height));

	[JSInvokable]
	public Task OnTitleChanged(string title) => _view.HandleTitleChangedAsync(title);

	[JSInvokable]
	public void OnContextMenu(double x, double y, bool hasSelection) => _view.ShowContextMenu(x, y, hasSelection);

	[JSInvokable]
	public Task OnFindRequested() => _view.OpenFindAsync().AsTask();

	[JSInvokable]
	public Task<bool> ConfirmPaste(string preview, int lineCount) => _view.ConfirmPasteAsync(preview, lineCount);

	[JSInvokable]
	public void OnSearchResults(int index, int count) => _view.HandleSearchResults(index, count);

	[JSInvokable]
	public void OnScrollStateChanged(bool atBottom) => _view.HandleScrollStateChanged(atBottom);

	[JSInvokable]
	public void OnRowHover(string text, int top, int height) => _view.HandleRowHover(text, top, height);
}
