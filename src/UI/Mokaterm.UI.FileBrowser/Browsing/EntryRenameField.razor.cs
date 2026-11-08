using Microsoft.AspNetCore.Components;
using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.UI.FileBrowser.Browsing;

public sealed partial class EntryRenameField : ComponentBase
{
	[Parameter]
	[EditorRequired]
	public RemoteFileEntry Entry { get; set; } = default!;

	/// <summary>Names the folder already uses, without the entry being renamed.</summary>
	[Parameter]
	public IReadOnlySet<string> TakenNames { get; set; } = new HashSet<string>(StringComparer.Ordinal);

	/// <summary>Runs the rename. What it throws shows under the field and keeps it open.</summary>
	[Parameter]
	[EditorRequired]
	public Func<string, Task> Save { get; set; } = default!;

	[Parameter]
	public EventCallback Closed { get; set; }

	private string FieldLabel => Entry.IsDirectoryLike ? "Folder name" : "File name";

	private int StemLength => EntryNames.Split(Entry.Name, Entry.IsDirectoryLike).Stem.Length;

	private string? Validate(string name) => EntryNames.ProblemWith(name, TakenNames);

	private Task OnEditingChanged(bool open) => open ? Task.CompletedTask : Closed.InvokeAsync();
}
