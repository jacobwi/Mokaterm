using Moka.Red.Core.Icons;

namespace Mokaterm.Modules.Serial;

/// <summary>
/// Icons this module needs, drawn like the ones in <c>MokatermIcons</c>: stroked paths on a 24 by 24 grid. The
/// plain terminal already stands for SSH, so a serial session gets a connector of its own.
/// </summary>
internal static class SerialIcons
{
	/// <summary>A D-sub connector with its pins and a cable, for sessions and for the serial settings page.</summary>
	public static readonly MokaIconDefinition Serial = new("mt-serial",
		"M4 6h16l-2.5 9h-11z M8 9.5h.01 M12 9.5h.01 M16 9.5h.01 M10 12h.01 M14 12h.01 M12 15v4");

	/// <summary>Two signal lines, one high and one low, for the panel that shows the control pins.</summary>
	public static readonly MokaIconDefinition Signals = new("mt-serial-signals",
		"M2 8h4V4h5v4h4V4h5 M2 16h5v4h4v-4h5v4h4");
}
