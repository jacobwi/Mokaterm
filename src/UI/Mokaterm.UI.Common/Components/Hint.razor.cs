using Microsoft.AspNetCore.Components;
using Moka.Red.Feedback.Tooltip;

namespace Mokaterm.UI.Common.Components;

/// <summary>
/// The app's tooltip for anything that is not an icon button: a chip, a status dot, a labelled button whose hint says
/// more than its label, or one that is greyed out and says why. It shows on hover and on keyboard focus, never after a
/// click, hides at once, and is placed against the window, so a panel that clips its overflow cannot cut it off.
/// </summary>
/// <remarks>
/// Use it instead of a <c>title</c> attribute for a hint the user is meant to read. A <c>title</c> stays right for
/// visible text that may be cut off, where hovering shows the whole value: a file name, a path, a long label.
/// The hint wraps its trigger in an element of its own, which is what a flex or grid layout then places, so a layout
/// class goes on <see cref="Class"/> rather than on the trigger.
/// </remarks>
public sealed partial class Hint : ComponentBase
{
	/// <summary>
	/// Below the trigger, so a hint in a row of icons never covers the icons beside it. Where there is no room the
	/// tooltip moves to the side that has some.
	/// </summary>
	public const MokaTooltipPosition DefaultPosition = MokaTooltipPosition.Bottom;

	/// <summary>The element the tooltip explains.</summary>
	[Parameter]
	public RenderFragment? ChildContent { get; set; }

	/// <summary>
	/// What the tooltip says. Null or empty shows nothing, so a reason that only exists while something is greyed out can
	/// be passed as it is.
	/// </summary>
	[Parameter]
	public string? Text { get; set; }

	/// <summary>A key that does the same, shown after the text: "Refresh (F5)".</summary>
	[Parameter]
	public string? Shortcut { get; set; }

	/// <summary>
	/// Where the tooltip goes, which must never be over the controls next to it: Bottom in a row (BottomStart or
	/// BottomEnd for the first and last one against a window or panel edge), Top in a row at the bottom of a view such
	/// as a footer, Left or Right only in a column such as a side rail.
	/// </summary>
	[Parameter]
	public MokaTooltipPosition Position { get; set; } = DefaultPosition;

	/// <summary>A class on the element that wraps the trigger, for its place in a layout.</summary>
	[Parameter]
	public string? Class { get; set; }

	/// <summary>An inline style on that element, such as <c>flex: 1</c> for a trigger that shares a row with others.</summary>
	[Parameter]
	public string? Style { get; set; }

	/// <summary>The tooltip's text: <paramref name="text"/>, with <paramref name="shortcut"/> after it in brackets.</summary>
	public static string? Format(string? text, string? shortcut) =>
		string.IsNullOrEmpty(text) ? null
		: string.IsNullOrEmpty(shortcut) ? text
		: $"{text} ({shortcut})";
}
