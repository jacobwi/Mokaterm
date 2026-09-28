using Microsoft.AspNetCore.Components;

namespace Mokaterm.UI.Common.Components;

/// <summary>One setting: label and helper text on the left, the control on the right (or below when <see cref="Stacked"/>).</summary>
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

	private string RowClass => Stacked ? "settings-row settings-row--stacked" : "settings-row";

	// Block layout lets fields and sliders fill the width; natural-size controls such as switches stay flex items.
	private string ControlClass => Stacked || ControlWidth is not null
		? "settings-row__control settings-row__control--fill"
		: "settings-row__control";

	private string? ControlStyle => ControlWidth is null ? null : $"width:{ControlWidth}";
}
