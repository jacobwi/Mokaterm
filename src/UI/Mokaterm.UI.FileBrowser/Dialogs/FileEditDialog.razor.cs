using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Moka.Red.ContextMenu;
using Moka.Red.Feedback.Dialog;
using Mokaterm.UI.Common.Interop;
using Mokaterm.UI.FileBrowser.Editing;
using Mokaterm.UI.FileBrowser.Interop;

namespace Mokaterm.UI.FileBrowser.Dialogs;

/// <summary>
/// Edits one remote text file in place. The save runs here rather than after the dialog closes, so a server that
/// refuses the write leaves the dialog open with the edits still in it instead of dropping them on the floor. The body
/// switches between the text, a review of the edits and, when the file changed on the server meanwhile, what saving
/// would change there. All three live in this one dialog because a second service dialog awaited from inside this one
/// would never return. Closes with true once a save went through, and cancels otherwise.
/// </summary>
public sealed partial class FileEditDialog : ComponentBase, IAsyncDisposable
{
	/// <summary>
	/// Past either of these the page leaves the text uncoloured, since every keystroke re-reads the whole box to find
	/// what changed. The page is handed both and applies the same rule, so the status bar and the box agree.
	/// </summary>
	internal const int MaxPaintCharacters = 256 * 1024;

	/// <inheritdoc cref="MaxPaintCharacters"/>
	internal const int MaxPaintLines = 20_000;

	/// <summary>Rows drawn for one comparison. Each is a few elements of render tree, so the rest are only counted.</summary>
	internal const int MaxDiffRows = 3000;

	/// <summary>What the page does with the keys a plain text area would spend elsewhere.</summary>
	private const string Hint = "Tab indents · Ctrl+S saves · Escape then Tab leaves the box";

	private ElementReference _frame;
	private ElementReference _area;
	private ElementReference _numbers;
	private ElementReference _lines;
	private ElementReference _diffBody;
	private DotNetObjectReference<FileEditorJsReceiver>? _receiver;
	private IJSObjectReference? _jsHandle;

	private string _text = "";

	// What the edits are measured against: the file as it was opened, or the server's copy a conflict showed once the
	// user went back to editing on top of it.
	private string _original = "";
	private bool _rebased;
	private IReadOnlyList<string> _facts = [];
	private string? _error;
	private bool _dirty;
	private bool _saving;
	private bool _disposed;

	private int _line = 1;
	private int _column = 1;
	private int _lineCount = 1;

	private EditorLanguage _language = EditorLanguages.Plain;
	private IReadOnlyList<MokaContextMenuItem> _languageMenu = [];
	private bool _tooLargeToPaint;

	private bool _reviewing;
	private FileSaveResult? _conflict;
	private LineDiffResult? _diff;
	private IReadOnlyList<DiffLine> _diffRows = [];
	private int _hiddenDiffLines;
	private int _diffDigits = 1;

	// Bumped by every comparison started and every return to the text, so a slow one that finishes late is dropped.
	private int _diffVersion;
	private bool _focusDiff;
	private bool _focusText;

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

	/// <summary>
	/// Writes the text. With the flag clear it first checks whether the file changed on the server since it was last
	/// read, and answers <see cref="FileSaveOutcome.Conflict"/> instead of writing when it did. With the flag set it
	/// writes over whatever is there.
	/// </summary>
	[Parameter, EditorRequired]
	public Func<string, bool, Task<FileSaveResult>> Save { get; set; } = default!;

	/// <summary>
	/// The user went back to editing from a conflict: the server copy it showed becomes what the next save compares
	/// against, so that save stops again only if the file changes once more.
	/// </summary>
	[Parameter, EditorRequired]
	public Action AcceptServerCopy { get; set; } = default!;

	/// <summary>Asked before throwing away unsaved edits.</summary>
	[Parameter, EditorRequired]
	public Func<Task<bool>> ConfirmDiscard { get; set; } = default!;

	private string FrameClass => _dirty ? "mt-edit-frame mt-edit-frame--dirty" : "mt-edit-frame";

	private bool ShowsDiff => _reviewing || _conflict is not null;

	private string CaretText => string.Create(CultureInfo.CurrentCulture, $"Ln {_line}, Col {_column}");

	private string LineCountText =>
		_lineCount == 1 ? "1 line" : string.Create(CultureInfo.CurrentCulture, $"{_lineCount:N0} lines");

	private bool ShowsTooLarge => _tooLargeToPaint && _language != EditorLanguages.Plain;

	private static string TooLargeTitle => string.Create(
		CultureInfo.CurrentCulture,
		$"Highlighting stops past {MaxPaintCharacters / 1024:N0} K characters or {MaxPaintLines:N0} lines, so typing stays quick.");

	private string LanguageLabel => "Highlighting: " + _language.Name;

	private string DiffTitle => _conflict is not null
		? "What saving would change on the server"
		: _rebased ? "Changes since the server's copy was read" : "Changes since the file was opened";

	/// <summary>The reason alone when the server's copy cannot be shown: one sentence, and the two buttons below it.</summary>
	private string ConflictMessage => _conflict is { ServerText: null, Message: { } reason }
		? reason
		: "Someone changed it after it was read here. Below is the server's copy against your edits.";

	private static string NoComparisonText =>
		"Overwrite writes your edits to the server anyway. Back to editing keeps them here unsaved, and the next save checks the server again.";

	private string SameLinesText => _conflict is not null
		? "The server's copy already matches your edits line for line."
		: "No line changed. Only the final line break differs, and saving keeps the one the file had.";

	private string DigitsStyle => string.Create(CultureInfo.InvariantCulture, $"--mt-diff-digits: {_diffDigits}");

	private string HiddenText => string.Create(
		CultureInfo.CurrentCulture,
		$"{_hiddenDiffLines:N0} more {(_hiddenDiffLines == 1 ? "line" : "lines")} not shown.");

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
	/// otherwise have the save write the copy from before the last keystroke. A conflict on screen waits for one of its
	/// own two answers instead.
	/// </summary>
	internal Task HandleSaveRequestedAsync(string text)
	{
		if (_disposed || _conflict is not null)
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
		_facts = Facts;
		_lineCount = CountLines(Content);
		_tooLargeToPaint = TooLargeToPaint(Content);
		_language = EditorLanguages.Detect(Path, Content);
		BuildLanguageMenu();
		_receiver = DotNetObjectReference.Create(new FileEditorJsReceiver(this));
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if (firstRender)
		{
			await AttachAsync();
		}

		if (_focusDiff)
		{
			_focusDiff = false;
			await FocusAsync(_diffBody);
		}
		else if (_focusText)
		{
			_focusText = false;
			await FocusAsync(_area);
		}
	}

	/// <summary>A line per newline, plus the one after the last of them, which is what a text area shows.</summary>
	private static int CountLines(string text) => text.AsSpan().Count('\n') + 1;

	private static bool TooLargeToPaint(string text) => text.Length > MaxPaintCharacters || CountLines(text) > MaxPaintLines;

	private static async Task FocusAsync(ElementReference element)
	{
		try
		{
			await element.FocusAsync();
		}
		catch (Exception ex) when (JsModule.IsExpected(ex))
		{
			// Focus is a convenience: the view it was meant for works without it.
		}
	}

	private async Task AttachAsync()
	{
		if (_receiver is null || _disposed)
		{
			return;
		}

		string language = _language.Id;
		try
		{
			IJSObjectReference handle = await Interop.AttachEditorAsync(
				_frame,
				_area,
				_numbers,
				_lines,
				_receiver,
				language,
				MaxPaintCharacters,
				MaxPaintLines,
				CancellationToken.None);
			if (_disposed)
			{
				await handle.ReleaseAsync();
				return;
			}

			_jsHandle = handle;
		}
		catch (Exception ex) when (JsModule.IsExpected(ex))
		{
			// Without the page side this is still a working text area, only without the gutter, colours, Tab and Ctrl+S.
			return;
		}

		// A language picked while the attach was on its way has not reached the page yet.
		if (!string.Equals(language, _language.Id, StringComparison.Ordinal))
		{
			await SendLanguageAsync();
		}
	}

	private void OnInput(ChangeEventArgs args) => SetText(args.Value as string ?? "");

	private void SetText(string text)
	{
		_text = text;
		_dirty = !string.Equals(_text, _original, StringComparison.Ordinal);
		_tooLargeToPaint = TooLargeToPaint(text);
	}

	private void BuildLanguageMenu()
	{
		List<MokaContextMenuItem> items = new(EditorLanguages.All.Count);
		foreach (EditorLanguage language in EditorLanguages.All)
		{
			items.Add(new MokaContextMenuItem
			{
				Text = language.Name,
				Checked = language == _language,

				// Plain text sits apart, as the way to turn colouring off rather than one more language.
				DividerBefore = items.Count == 1,
				OnClick = () => SetLanguageAsync(language),
			});
		}

		_languageMenu = items;
	}

	private async Task SetLanguageAsync(EditorLanguage language)
	{
		if (_disposed || language == _language)
		{
			return;
		}

		_language = language;
		BuildLanguageMenu();

		// The menu runs this from its own host, which re-renders itself and not this dialog.
		StateHasChanged();
		await SendLanguageAsync();
	}

	private async Task SendLanguageAsync()
	{
		if (_jsHandle is not { } handle)
		{
			return;
		}

		try
		{
			await handle.InvokeVoidAsync("setLanguage", _language.Id);
		}
		catch (Exception ex) when (JsModule.IsExpected(ex))
		{
			// The box keeps whatever colours it had, and its text stays editable either way.
		}
	}

	private async Task ReviewAsync()
	{
		if (_saving || !_dirty || ShowsDiff)
		{
			return;
		}

		_reviewing = true;
		_focusDiff = true;
		await CompareAsync(_original, _text);
	}

	private void BackToEditing()
	{
		if (_saving)
		{
			return;
		}

		if (_conflict is { } conflict)
		{
			AcceptServerCopy();

			// An unreadable copy cannot become the text the review compares against; the stamp moved all the same.
			if (conflict.ServerText is { } serverText)
			{
				_original = serverText;
				_facts = conflict.ServerFacts ?? _facts;
				_rebased = true;
				_dirty = !string.Equals(_text, _original, StringComparison.Ordinal);
			}

			_conflict = null;
		}

		_reviewing = false;
		_diffVersion++;
		_diff = null;
		_diffRows = [];
		_focusText = true;
	}

	/// <summary>
	/// Compares off the render thread: the search is capped, but two long files that differ throughout still take a
	/// noticeable moment, and the circuit would sit still for all of it.
	/// </summary>
	private async Task CompareAsync(string before, string after)
	{
		int version = ++_diffVersion;
		_diff = null;
		_diffRows = [];
		_hiddenDiffLines = 0;
		_diffDigits = Math.Max(CountLines(before), CountLines(after)).ToString(CultureInfo.InvariantCulture).Length;
		StateHasChanged();

		LineDiffResult diff = await Task.Run(() => LineDiff.Compare(before, after));
		if (_disposed || version != _diffVersion)
		{
			return;
		}

		_diff = diff;
		if (diff.Lines.Count > MaxDiffRows)
		{
			_diffRows = diff.Lines.Take(MaxDiffRows).ToArray();
			_hiddenDiffLines = diff.Lines.Skip(MaxDiffRows).Count(line => line.Kind != DiffLineKind.Skipped);
		}
		else
		{
			_diffRows = diff.Lines;
		}

		StateHasChanged();
	}

	private Task SaveAsync() => SaveCoreAsync(overwrite: false);

	private Task OverwriteAsync() => SaveCoreAsync(overwrite: true);

	private async Task SaveCoreAsync(bool overwrite)
	{
		if (_saving || (!_dirty && !overwrite))
		{
			return;
		}

		_saving = true;
		_error = null;

		// Rendered here rather than left to Blazor's own post-event render, because Ctrl+S arrives from the page and
		// nothing would otherwise show the button as busy.
		StateHasChanged();
		FileSaveResult result;
		try
		{
			result = await Save(_text, overwrite);
		}
		finally
		{
			_saving = false;
		}

		switch (result.Outcome)
		{
			case FileSaveOutcome.Saved:
				StateHasChanged();
				Dialog?.Close(true);
				return;

			case FileSaveOutcome.Conflict:
				_conflict = result;
				_reviewing = false;
				_focusDiff = true;
				if (result.ServerText is { } serverText)
				{
					await CompareAsync(serverText, _text);
					return;
				}

				_diffVersion++;
				_diff = null;
				_diffRows = [];
				break;

			default:
				_error = result.Message ?? "The file could not be written.";
				break;
		}

		StateHasChanged();
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

	private static string RowClass(DiffLineKind kind) => kind switch
	{
		DiffLineKind.Added => "mt-diff-row mt-diff-row--added",
		DiffLineKind.Removed => "mt-diff-row mt-diff-row--removed",
		_ => "mt-diff-row",
	};

	private static string Sign(DiffLineKind kind) => kind switch
	{
		DiffLineKind.Added => "+",
		DiffLineKind.Removed => "-",
		_ => " ",
	};

	private static string NumberText(int number) => number > 0 ? number.ToString(CultureInfo.InvariantCulture) : "";

	private static string SkippedText(int count) =>
		count == 1 ? "1 unchanged line" : string.Create(CultureInfo.CurrentCulture, $"{count:N0} unchanged lines");

	private static string SummaryTitle(LineDiffResult diff) => string.Create(
		CultureInfo.CurrentCulture,
		$"{diff.Added:N0} {(diff.Added == 1 ? "line" : "lines")} added, {diff.Removed:N0} removed");
}
