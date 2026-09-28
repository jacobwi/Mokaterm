using System.Globalization;
using Microsoft.AspNetCore.Components;
using Moka.Red.Feedback.Dialog;
using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.UI.FileBrowser.Dialogs;

/// <summary>Confirms a delete, listing the names and saying whether folders go with their contents. Closes with true.</summary>
public sealed partial class DeleteDialog : ComponentBase
{
	private const int MaxListed = 50;

	[CascadingParameter]
	public MokaDialogContext? Dialog { get; set; }

	[Parameter, EditorRequired]
	public IReadOnlyList<RemoteFileEntry> Entries { get; set; } = [];

	/// <summary>The delete runs on the elevated file system.</summary>
	[Parameter]
	public bool AsRoot { get; set; }

	private bool Recursive => Entries.Any(entry => entry.Kind == RemoteEntryKind.Directory);

	private string Summary => Entries.Count == 1
		? "Delete this item? This cannot be undone."
		: string.Create(CultureInfo.CurrentCulture, $"Delete these {Entries.Count} items? This cannot be undone.");

	private string MoreText => string.Create(CultureInfo.CurrentCulture, $"and {Entries.Count - MaxListed} more");

	private void Confirm() => Dialog?.Close(true);

	private void Cancel() => Dialog?.Cancel();
}
