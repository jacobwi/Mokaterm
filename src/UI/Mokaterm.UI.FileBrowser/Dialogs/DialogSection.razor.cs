using Microsoft.AspNetCore.Components;

namespace Mokaterm.UI.FileBrowser.Dialogs;

/// <summary>A group of fields in a dialog under an uppercase micro-label, set off from the group above by a rule.</summary>
public sealed partial class DialogSection : ComponentBase
{
	private const string TitleStyle = "letter-spacing:0.08em;color:var(--moka-color-on-surface-tertiary)";

	[Parameter, EditorRequired]
	public string Title { get; set; } = "";

	[Parameter]
	public RenderFragment? ChildContent { get; set; }
}
