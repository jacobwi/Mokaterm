using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.UI.Common.Interop;

namespace Mokaterm.UI.Terminal.Interop;

/// <summary>Calls into terminal.js. Scoped, so the module is imported once per circuit or WebView.</summary>
internal sealed class TerminalInterop : IAsyncDisposable
{
	private const string ModulePath = "./_content/Mokaterm.UI.Terminal/js/terminal.js";

	// Replies from JS count against SignalR's receive limit on Blazor Server (32 KB by default). Selection text
	// takes at most 3 UTF-8 bytes per UTF-16 unit, so 8192 units per reply stays below it.
	private const int SelectionChunkLength = 8192;

	private readonly JsModule _module;

	public TerminalInterop(IJSRuntime jsRuntime) => _module = new JsModule(jsRuntime, ModulePath);

	public ValueTask<TerminalCreateResult> CreateAsync(
		ElementReference host,
		ElementReference screen,
		DotNetObjectReference<TerminalJsCallbacks> callbacks,
		TerminalJsOptions options,
		TerminalSize initialSize) =>
		_module.InvokeAsync<TerminalCreateResult>("create", host, screen, callbacks, options, initialSize.Columns, initialSize.Rows);

	/// <summary>Completes when xterm.js has parsed <paramref name="data"/>.</summary>
	public ValueTask WriteAsync(int id, byte[] data, CancellationToken cancellationToken) =>
		_module.InvokeVoidAsync("write", cancellationToken, id, data);

	public ValueTask SetOptionsAsync(int id, IReadOnlyDictionary<string, object?> changes) =>
		_module.InvokeVoidAsync("setOptions", id, changes);

	public ValueTask FitAsync(int id) => _module.InvokeVoidAsync("fit", id);

	public ValueTask FocusAsync(int id) => _module.InvokeVoidAsync("focus", id);

	public ValueTask ClearAsync(int id) => _module.InvokeVoidAsync("clear", id);

	/// <summary>Full terminal reset, for when the view switches to a different stream.</summary>
	public ValueTask ResetAsync(int id) => _module.InvokeVoidAsync("reset", id);

	public ValueTask SelectAllAsync(int id) => _module.InvokeVoidAsync("selectAll", id);

	/// <summary>
	/// Reads the clipboard in the page and pastes it through xterm.js, which asks back for confirmation when the text spans
	/// lines. Throws a <see cref="JSException"/> when the page may not read the clipboard.
	/// </summary>
	public ValueTask PasteFromClipboardAsync(int id) => _module.InvokeVoidAsync("pasteFromClipboard", id);

	public async ValueTask<string> GetSelectionAsync(int id)
	{
		SelectionChunk chunk = await _module.InvokeAsync<SelectionChunk>("getSelection", id, 0, SelectionChunkLength);
		if (chunk.Next < 0)
		{
			return chunk.Text;
		}

		StringBuilder text = new(chunk.Text);
		while (chunk.Next >= 0)
		{
			chunk = await _module.InvokeAsync<SelectionChunk>("getSelection", id, chunk.Next, SelectionChunkLength);
			text.Append(chunk.Text);
		}

		return text.ToString();
	}

	public ValueTask<TerminalSearchResult> FindNextAsync(int id, string term, bool incremental, TerminalSearchFlags flags) =>
		_module.InvokeAsync<TerminalSearchResult>("findNext", id, term, incremental, flags);

	public ValueTask<TerminalSearchResult> FindPreviousAsync(int id, string term, TerminalSearchFlags flags) =>
		_module.InvokeAsync<TerminalSearchResult>("findPrevious", id, term, flags);

	public ValueTask ScrollToBottomAsync(int id) => _module.InvokeVoidAsync("scrollToBottom", id);

	public ValueTask ClearSearchAsync(int id) => _module.InvokeVoidAsync("clearSearch", id);

	public ValueTask FocusFindInputAsync(ElementReference host) => _module.InvokeVoidAsync("focusFindInput", host);

	/// <summary>Disposes the xterm.js instance. False when the circuit or WebView is already gone.</summary>
	public ValueTask<bool> TryDisposeInstanceAsync(int id) => _module.TryInvokeVoidAsync("dispose", id);

	public ValueTask DisposeAsync() => _module.DisposeAsync();
}
