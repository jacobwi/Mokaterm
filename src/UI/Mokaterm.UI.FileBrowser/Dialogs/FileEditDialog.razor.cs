using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Moka.Red.Feedback.Dialog;
using Mokaterm.UI.Common.Interop;
using Mokaterm.UI.FileBrowser.Interop;

namespace Mokaterm.UI.FileBrowser.Dialogs;

/// <summary>
/// Edits one remote text file in place. The save runs here rather than after the dialog closes, so a server that
/// refuses the write leaves the dialog open with the edits still in it instead of dropping them on the floor.
/// Closes with true once a save went through, and cancels otherwise.
/// </summary>
public sealed partial class FileEditDialog : ComponentBase, IAsyncDisposable
{
	/// <summary>What the page does with the keys a plain text area would spend elsewhere.</summary>
	private const string Hint = "Tab indents · Ctrl+S saves · Escape then Tab leaves the box";

	private ElementReference _frame;
	private ElementReference _area;
	private ElementReference _numbers;
	private DotNetObjectReference<FileEditorJsReceiver>? _receiver;
	private IJSObjectReference? _jsHandle;

	private string _text = "";
	private string _original = "";
	private string? _error;
	private bool _dirty;
	private bool _saving;
	private bool _disposed;

	private int _line = 1;
	private int _column = 1;
	private int _lineCount = 1;

	[Inject]
	private FileBrowserInterop Interop { get; set; } = default!;

	[CascadingParameter]
	public MokaDialogContext? Dialog { get; set; }

	[Parameter, EditorRequired]
	public string FileName { get; set; } = "";

	[Parameter, EditorRequired]
	public string Path { get; set; } = "";

	[Parameter, EditorRequired]
	public string Content { get; set; } = "";

	/// <summary>Size, encoding and line ending, already worded, one chip each.</summary>
	[Parameter]
	public IReadOnlyList<string> Facts { get; set; } = [];

	/// <summary>Writes the file. Returns the message to show when it failed, or null once it is saved.</summary>
	[Parameter, EditorRequired]
	public Func<string, Task<string?>> Save { get; set; } = default!;

	/// <summary>Asked before throwing away unsaved edits.</summary>
	[Parameter, EditorRequired]
	public Func<Task<bool>> ConfirmDiscard { get; set; } = default!;

	private string FrameClass => _dirty ? "mt-edit-frame mt-edit-frame--dirty" : "mt-edit-frame";

	private string CaretText => string.Create(CultureInfo.CurrentCulture, $"Ln {_line}, Col {_column}");

	private string LineCountText =>
		_lineCount == 1 ? "1 line" : string.Create(CultureInfo.CurrentCulture, $"{_lineCount:N0} lines");

	public async ValueTask DisposeAsync()
	{
		_disposed = true;
		await Interlocked.Exchange(ref _jsHandle, null).ReleaseAsync();
		Interlocked.Exchange(ref _receiver, null)?.Dispose();
	}

	/// <summary>Where the caret sits and how many lines the box holds, all one-based, as the page counts them.</summary>
	internal void HandleCaretMoved(int line, int column, int lineCount)
	{
		if (_disposed)
		{
			return;
		}

		_line = Math.Max(1, line);
		_column = Math.Max(1, column);
		_lineCount = Math.Max(1, lineCount);
		StateHasChanged();
	}

	/// <summary>
	/// Ctrl+S in the page. The text comes with it because an input event still on its way to this circuit would
	/// otherwise have the save write the copy from before the last keystroke.
	/// </summary>
	internal Task HandleSaveRequestedAsync(string text)
	{
		if (_disposed)
		{
			return Task.CompletedTask;
		}

		SetText(text);
		return SaveAsync();
	}

	protected override void OnInitialized()
	{
		_original = Content;
		_text = Content;
		_lineCount = CountLines(Content);
		_receiver = DotNetObjectReference.Create(new FileEditorJsReceiver(this));
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if (!firstRender || _receiver is null || _disposed)
		{
			return;
		}

		try
		{
			IJSObjectReference handle = await Interop.AttachEditorAsync(_frame, _area, _numbers, _receiver, CancellationToken.None);
			if (_disposed)
			{
				await handle.ReleaseAsync();
				return;
			}

			_jsHandle = handle;
		}
		catch (Exception ex) when (JsModule.IsExpected(ex))
		{
			// Without the page side this is still a working text area, only without the gutter, Tab and Ctrl+S.
		}
	}

	/// <summary>A line per newline, plus the one after the last of them, which is what a text area shows.</summary>
	private static int CountLines(string text) => text.AsSpan().Count('\n') + 1;

	private void OnInput(ChangeEventArgs args) => SetText(args.Value as string ?? "");

	private void SetText(string text)
	{
		_text = text;
		_dirty = !string.Equals(_text, _original, StringComparison.Ordinal);
	}

	private async Task SaveAsync()
	{
		if (_saving || !_dirty)
		{
			return;
		}

		_saving = true;
		_error = null;

		// Rendered here rather than left to Blazor's own post-event render, because Ctrl+S arrives from the page and
		// nothing would otherwise show the button as busy.
		StateHasChanged();
		try
		{
			_error = await Save(_text);
		}
		finally
		{
			_saving = false;
		}

		StateHasChanged();
		if (_error is null)
		{
			Dialog?.Close(true);
		}
	}

	private async Task CancelAsync()
	{
		if (_saving)
		{
			return;
		}

		if (_dirty && !await ConfirmDiscard())
		{
			return;
		}

		Dialog?.Cancel();
	}
}
