using Moka.Red.Core.Icons;

namespace Mokaterm.Modules.Telnet;

/// <summary>
/// Icons this module needs, drawn like the ones in <c>MokatermIcons</c>: stroked paths on a 24 by 24 grid. They
/// live here because the plain terminal already stands for SSH.
/// </summary>
public static class TelnetIcons
{
	/// <summary>A framed terminal window with a prompt, for sessions and for the telnet settings page.</summary>
	public static readonly MokaIconDefinition Telnet = new("mt-telnet",
		"M3 4h18a1 1 0 0 1 1 1v14a1 1 0 0 1-1 1H3a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1z M2 8h20 M6 12l2.5 2.5L6 17 M12 17h6");
}
