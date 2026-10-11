using Moka.Red.Core.Icons;

namespace Mokaterm.Modules.Local;

/// <summary>
/// Icons this module needs, drawn like the ones in <c>MokatermIcons</c>: stroked paths on a 24 by 24 grid. The plain
/// terminal already stands for SSH, so a shell on this computer gets a screen with a prompt on it, and the container and
/// pod shells the shapes their tools are known by.
/// </summary>
internal static class LocalIcons
{
	/// <summary>A monitor on its stand with a prompt inside, for sessions and for the local shell settings page.</summary>
	public static readonly MokaIconDefinition Shell = new("mt-local-shell",
		"M4 3h16a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z M8 21h8 M12 17v4 M6.5 7.5L9 10l-2.5 2.5 M11.5 12.5h5");

	/// <summary>A box seen from a corner, for a shell in a Docker container.</summary>
	public static readonly MokaIconDefinition Container = new("mt-local-container",
		"M12 2.5l8.5 4.75v9.5L12 21.5l-8.5-4.75v-9.5z M12 12l8.5-4.75 M12 12v9.5 M12 12L3.5 7.25");

	/// <summary>A seven sided wheel with its spokes, for a shell in a Kubernetes pod.</summary>
	public static readonly MokaIconDefinition Pod = new("mt-local-pod",
		"M12 2.5L19.4 6.1 21.3 14.1 16.1 20.6H7.9L2.7 14.1 4.6 6.1z M12 9.5a2.5 2.5 0 1 0 0 5 2.5 2.5 0 1 0 0-5z M12 9.5V5.5 M13.95 10.44l3.13-2.49 M14.44 12.56l3.9.89 M13.08 14.25l1.74 3.61 M10.92 14.25l-1.74 3.61 M9.56 12.56l-3.9.89 M10.05 10.44L6.92 7.95");
}
