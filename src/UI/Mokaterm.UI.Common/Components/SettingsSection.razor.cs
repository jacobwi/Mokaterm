using Microsoft.AspNetCore.Components;

namespace Mokaterm.UI.Common.Components;

/// <summary>A group of settings under an uppercase micro-label, with its rows in one outlined card.</summary>
public sealed partial class SettingsSection : ComponentBase
{
	[Parameter, EditorRequired]
	public string Title { get; set; } = "";

	[Parameter]
	public string? Description { get; set; }

	/// <summary>Buttons aligned with the heading.</summary>
	[Parameter]
	public RenderFragment? Actions { get; set; }

	[Parameter]
	public RenderFragment? ChildContent { get; set; }

	/// <summary>Renders the content without the card, for tables and grids that draw their own frame.</summary>
	[Parameter]
	public bool Flush { get; set; }
}
