using Mokaterm.UI.Common.Components;
using System.Globalization;
using Microsoft.AspNetCore.Components;
using Moka.Red.Feedback.Dialog;
using Mokaterm.UI.FileBrowser.Browsing;

namespace Mokaterm.UI.FileBrowser.Dialogs;

/// <summary>
/// Asks once for a whole paste what to do with the names the target folder already has: keep both, overwrite, or stop.
/// Enter keeps both, the answer that loses nothing. Closes with a <see cref="PasteConflictChoice"/>.
/// </summary>
public sealed partial class PasteConflictDialog : ComponentBase
{
	private const int MaxListed = 50;

	[CascadingParameter]
	public MokaDialogContext? Dialog { get; set; }

	/// <summary>The header the service's title-less dialog shows, from <see cref="FileBrowserDialogs"/>.</summary>
	[Parameter]
	public DialogHeading? Heading { get; set; }

	/// <summary>The names already taken in <see cref="Directory"/>.</summary>
	[Parameter, EditorRequired]
	public IReadOnlyList<string> Names { get; set; } = [];

	/// <summary>The folder the paste goes into.</summary>
	[Parameter]
	public string Directory { get; set; } = "";

	/// <summary>The copies are made by root.</summary>
	[Parameter]
	public bool AsRoot { get; set; }

	private string Summary => Names.Count == 1
		? $"\"{Names[0]}\" already exists in {Directory}."
		: string.Create(CultureInfo.CurrentCulture, $"{Names.Count} of the pasted items already exist in {Directory}.");

	private string MoreText => string.Create(CultureInfo.CurrentCulture, $"and {Names.Count - MaxListed} more");

	private void KeepBoth() => Dialog?.Close(PasteConflictChoice.KeepBoth);

	private void Overwrite() => Dialog?.Close(PasteConflictChoice.Overwrite);

	private void Cancel() => Dialog?.Cancel();
}
