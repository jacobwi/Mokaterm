using Moka.Red.Core.Icons;

namespace Mokaterm.Modules.Rdp;

/// <summary>
/// Icons this module needs, drawn like the ones in <c>MokatermIcons</c>: one stroked path on a 24 by 24 grid.
/// They live here because the VNC module already took the plain monitor.
/// </summary>
public static class RdpIcons
{
	/// <summary>A screen with a window on it, for RDP sessions and the RDP settings page.</summary>
	public static readonly MokaIconDefinition RemoteDesktop = new("mt-rdp",
		"M3 4h18a1 1 0 0 1 1 1v11a1 1 0 0 1-1 1H3a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1z M8 21h8 M12 17v4 M6 8h8 M6 11h5");
}
