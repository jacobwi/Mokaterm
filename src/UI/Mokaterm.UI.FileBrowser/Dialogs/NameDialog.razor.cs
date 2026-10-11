using Microsoft.AspNetCore.Components;
using Moka.Red.Feedback.Dialog;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.UI.FileBrowser.Browsing;

namespace Mokaterm.UI.FileBrowser.Dialogs;

/// <summary>Asks for a file or folder name, for renaming, new entries and archives. Closes with the name as a string.</summary>
public sealed partial class NameDialog : ComponentBase
{
	private string _name = "";
	private int _selectionEnd;

	[CascadingParameter]
	public MokaDialogContext? Dialog { get; set; }

	[Parameter]
	public string Label { get; set; } = "Name";

	[Parameter]
	public string InitialName { get; set; } = "";

	[Parameter]
	public string ConfirmText { get; set; } = "OK";

	/// <summary>Names already used in the folder, which the new name must not repeat. Leave out the entry being renamed.</summary>
	[Parameter]
	public IReadOnlySet<string> TakenNames { get; set; } = new HashSet<string>(StringComparer.Ordinal);

	/// <summary>Select the name without its extension, so typing replaces just the base name.</summary>
	[Parameter]
	public bool SelectBaseName { get; set; }

	private bool IsValid => _name.Length > 0 && Error is null;

	private string? Error =>
		_name.Length == 0 ? null
		: string.IsNullOrWhiteSpace(_name) ? "The name cannot be only spaces."
		: _name is "." or ".." ? "That name is reserved."
		: !RemotePath.IsValidName(_name) ? "Names cannot contain '/'."
		: TakenNames.Contains(_name) ? "Something with that name already exists here."
		: null;

	protected override void OnInitialized()
	{
		_name = InitialName;

		// ".bashrc" has no base name to keep, so it is selected whole, and "site.tar.gz" keeps its whole extension.
		_selectionEnd = SelectBaseName ? EntryNames.Split(InitialName, isFolder: false).Stem.Length : InitialName.Length;
	}

	private void OnNameChanged(string value) => _name = value;

	private void Confirm()
	{
		if (IsValid)
		{
			Dialog?.Close(_name);
		}
	}

	private void Cancel() => Dialog?.Cancel();
}
