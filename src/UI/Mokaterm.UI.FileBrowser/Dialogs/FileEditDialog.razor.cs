using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Moka.Red.Feedback.Dialog;
using Mokaterm.Abstractions.Interaction;

namespace Mokaterm.UI.FileBrowser.Dialogs;

/// <summary>
/// Edits one remote text file in place. The save runs here rather than after the dialog closes, so a server that
/// refuses the write leaves the dialog open with the edits still in it instead of dropping them on the floor.
/// Closes with true once a save went through, and cancels otherwise.
/// </summary>
public sealed partial class FileEditDialog : ComponentBase
{
	private string _text = "";
	private string _original = "";
	private string? _error;
	private bool _dirty;
	private bool _saving;

	[CascadingParameter]
	public MokaDialogContext? Dialog { get; set; }

	[Parameter, EditorRequired]
	public string FileName { get; set; } = "";

	[Parameter, EditorRequired]
	public string Path { get; set; } = "";

	[Parameter, EditorRequired]
	public string Content { get; set; } = "";

	/// <summary>Size, encoding and line ending, already worded, for the line under the path.</summary>
	[Parameter]
	public string Facts { get; set; } = "";

	/// <summary>Writes the file. Returns the message to show when it failed, or null once it is saved.</summary>
	[Parameter, EditorRequired]
	public Func<string, Task<string?>> Save { get; set; } = default!;

	/// <summary>Asked before throwing away unsaved edits.</summary>
	[Parameter, EditorRequired]
	public Func<Task<bool>> ConfirmDiscard { get; set; } = default!;

	protected override void OnInitialized()
	{
		_original = Content;
		_text = Content;
	}

	private void OnInput(ChangeEventArgs args)
	{
		_text = args.Value as string ?? "";
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
		try
		{
			_error = await Save(_text);
		}
		finally
		{
			_saving = false;
		}

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
