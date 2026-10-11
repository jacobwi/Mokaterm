using Microsoft.AspNetCore.Components;

namespace Mokaterm.UI.Common.Components;

/// <summary>
/// One setting: label and helper text on the left, the control on the right (or below when <see cref="Stacked"/>). The
/// settings search finds the row by the label and description its page's markup writes out, so a row added to any page
/// can be searched for without being listed anywhere else.
/// </summary>
public sealed partial class SettingsRow : ComponentBase
{
	[Parameter, EditorRequired]
	public string Label { get; set; } = "";

	[Parameter]
	public string? Description { get; set; }

	[Parameter]
	public RenderFragment? ChildContent { get; set; }

	/// <summary>Puts the control under the text at full width, for option lists and grids.</summary>
	[Parameter]
	public bool Stacked { get; set; }

	/// <summary>Fixed width for the control, for example <c>240px</c>. Fields and sliders stretch to fill it.</summary>
	[Parameter]
	public string? ControlWidth { get; set; }

	/// <summary>
	/// False keeps the row out of the settings search and gives it no anchor, for a row that shows what is loaded (a
	/// module, a credit) or a message rather than a setting.
	/// </summary>
	[Parameter]
	public bool Searchable { get; set; } = true;

	private string RowClass => Stacked ? "settings-row settings-row--stacked" : "settings-row";

	// What a settings search result scrolls to.
	private string? Anchor => Searchable ? SettingsAnchor.Of(Label) : null;

	// Block layout lets fields and sliders fill the width; natural-size controls such as switches stay flex items.
	private string ControlClass => Stacked || ControlWidth is not null
		? "settings-row__control settings-row__control--fill"
		: "settings-row__control";

	private string? ControlStyle => ControlWidth is null ? null : $"width:{ControlWidth}";
}
