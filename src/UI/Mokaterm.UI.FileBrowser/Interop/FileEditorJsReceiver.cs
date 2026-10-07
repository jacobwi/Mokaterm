using Microsoft.JSInterop;
using Mokaterm.UI.FileBrowser.Dialogs;

namespace Mokaterm.UI.FileBrowser.Interop;

/// <summary>
/// Receives the file editor's calls from <c>filebrowser.js</c>. JS-invokable methods must be public, so they live here
/// instead of on the dialog, whose public surface other projects compile against.
/// </summary>
internal sealed class FileEditorJsReceiver
{
	private readonly FileEditDialog _dialog;

	public FileEditorJsReceiver(FileEditDialog dialog) => _dialog = dialog;

	/// <summary>The caret moved or the text changed shape. Line and column are one-based.</summary>
	[JSInvokable]
	public void OnCaretMoved(int line, int column, int lineCount) => _dialog.HandleCaretMoved(line, column, lineCount);

	/// <summary>Ctrl+S, carrying the text as the page has it.</summary>
	[JSInvokable]
	public Task OnSaveRequested(string text) => _dialog.HandleSaveRequestedAsync(text);
}
