using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Moka.Red.Core.Enums;
using Moka.Red.Core.Icons;
using Moka.Red.Feedback.Tooltip;

namespace Mokaterm.UI.Common.Components;

/// <summary>
/// A button that shows only an icon, with its name in a tooltip. Every icon-only button in the app is one of these, so
/// they all explain themselves the same way: on hover and on keyboard focus, never after a click, and still while the
/// button is greyed out, which is when the reason in the tooltip matters most.
/// </summary>
/// <remarks>
/// <see cref="Label"/> is both the tooltip and the button's accessible name; <see cref="AriaLabel"/> replaces the name
/// alone where a row's button needs to say which row it acts on. Never give one a <c>title</c> as well: the browser's own
/// tooltip would show beside this one.
/// </remarks>
public sealed partial class IconButton : ComponentBase
{
	[Parameter, EditorRequired]
	public MokaIconDefinition Icon { get; set; }

	/// <summary>What the button does, such as "Refresh". The tooltip shows it and a screen reader reads it.</summary>
	[Parameter, EditorRequired]
	public string Label { get; set; } = "";

	/// <summary>
	/// The accessible name when it should say more than the tooltip, such as "Delete web01" for a button whose tooltip is
	/// "Delete". Null uses <see cref="Label"/>.
	/// </summary>
	[Parameter]
	public string? AriaLabel { get; set; }

	/// <summary>A key that does the same, shown after the label in the tooltip: "Refresh (F5)".</summary>
	[Parameter]
	public string? Shortcut { get; set; }

	/// <summary>Where the tooltip goes; <see cref="Hint.Position"/> has the rule.</summary>
	[Parameter]
	public MokaTooltipPosition Position { get; set; } = Hint.DefaultPosition;

	[Parameter]
	public EventCallback<MouseEventArgs> OnClick { get; set; }

	[Parameter]
	public bool Disabled { get; set; }

	/// <summary>Shows a spinner in place of the icon and turns the button off while it runs.</summary>
	[Parameter]
	public bool Loading { get; set; }

	[Parameter]
	public MokaVariant Variant { get; set; } = MokaVariant.Text;

	[Parameter]
	public MokaColor Color { get; set; } = MokaColor.Surface;

	[Parameter]
	public MokaSize Size { get; set; } = MokaSize.Sm;

	/// <summary>Colors the icon with the accent, for a panel that is open or a toggle that is on.</summary>
	[Parameter]
	public bool Active { get; set; }

	/// <summary>Makes the button a toggle for screen readers, pressed or not. Null leaves it a plain button.</summary>
	[Parameter]
	public bool? Pressed { get; set; }

	/// <summary>
	/// A small glowing dot on the icon's corner, in this CSS colour, for something going on behind the button such as a
	/// recording. The label says what it means.
	/// </summary>
	[Parameter]
	public string? Dot { get; set; }

	/// <summary>A class on the element that wraps the button, for its place in a layout (hiding it in a narrow toolbar).</summary>
	[Parameter]
	public string? Class { get; set; }

	/// <summary>A class on the button itself, for how it looks.</summary>
	[Parameter]
	public string? ButtonClass { get; set; }

	/// <summary>Anything else goes on the button: <c>aria-haspopup</c>, <c>tabindex</c>, <c>data-</c> attributes.</summary>
	[Parameter(CaptureUnmatchedValues = true)]
	public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

	private string? PressedText => Pressed switch
	{
		true => "true",
		false => "false",
		null => null,
	};
}
