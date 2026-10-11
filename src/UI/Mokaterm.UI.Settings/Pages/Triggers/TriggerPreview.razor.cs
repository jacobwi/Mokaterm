using Microsoft.AspNetCore.Components;

namespace Mokaterm.UI.Settings.Pages.Triggers;

/// <summary>A sample line drawn with the colors the trigger rules would give it in a terminal.</summary>
public sealed partial class TriggerPreview : ComponentBase
{
	[Parameter, EditorRequired]
	public string Text { get; set; } = "";

	/// <summary>One per rule, in the order the rules run.</summary>
	[Parameter, EditorRequired]
	public IReadOnlyList<TriggerLayer> Layers { get; set; } = [];
}
