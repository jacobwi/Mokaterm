using Moka.Red.Core.Icons;

namespace Mokaterm.Modules.Ssh.Components.Docker;

/// <summary>The Docker manager's icons, drawn like the rest: single stroked paths on a 24x24 grid.</summary>
internal static class DockerIcons
{
	/// <summary>A box seen from a corner, for containers and for the manager itself.</summary>
	public static readonly MokaIconDefinition Container = new("mt-docker-container",
		"M12 2.5l8.5 4.75v9.5L12 21.5l-8.5-4.75v-9.5z M12 12l8.5-4.75 M12 12v9.5 M12 12L3.5 7.25");

	/// <summary>Three stacked sheets, for compose projects.</summary>
	public static readonly MokaIconDefinition Compose = new("mt-docker-compose",
		"M12 3l9 4.5-9 4.5-9-4.5z M3 12l9 4.5 9-4.5 M3 16.5l9 4.5 9-4.5");

	/// <summary>A framed stack of layers, for images.</summary>
	public static readonly MokaIconDefinition Image = new("mt-docker-image",
		"M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z M3 9h18 M3 15h18");

	/// <summary>A drum, for volumes.</summary>
	public static readonly MokaIconDefinition Volume = new("mt-docker-volume",
		"M4 6c0-1.7 3.6-3 8-3s8 1.3 8 3-3.6 3-8 3-8-1.3-8-3z M4 6v12c0 1.7 3.6 3 8 3s8-1.3 8-3V6 M4 12c0 1.7 3.6 3 8 3s8-1.3 8-3");

	/// <summary>Three joined nodes, for networks.</summary>
	public static readonly MokaIconDefinition Network = new("mt-docker-network",
		"M12 3a2 2 0 1 0 0 4 2 2 0 1 0 0-4z M5 17a2 2 0 1 0 0 4 2 2 0 1 0 0-4z M19 17a2 2 0 1 0 0 4 2 2 0 1 0 0-4z M12 7v5 M12 12l-5.5 5.2 M12 12l5.5 5.2");

	/// <summary>Four tiles, for the overview.</summary>
	public static readonly MokaIconDefinition Overview = new("mt-docker-overview",
		"M3 3h8v8H3z M13 3h8v5h-8z M13 10h8v11h-8z M3 13h8v8H3z");

	/// <summary>A filled square's outline, for stop.</summary>
	public static readonly MokaIconDefinition Stop = new("mt-docker-stop", "M6 6h12v12H6z");

	/// <summary>A bolt, for kill.</summary>
	public static readonly MokaIconDefinition Kill = new("mt-docker-kill", "M13 2L4 14h7l-1 8 9-12h-7z");

	/// <summary>Bars of three heights, for stats.</summary>
	public static readonly MokaIconDefinition Stats = new("mt-docker-stats", "M4 20V12 M10 20V5 M16 20v-9 M22 20H2");

	/// <summary>An arrow rising out of a circle, for image updates.</summary>
	public static readonly MokaIconDefinition Update = new("mt-docker-update",
		"M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18z M12 16.5V8 M8.5 11.5L12 8l3.5 3.5");

	/// <summary>A broom, for prune.</summary>
	public static readonly MokaIconDefinition Prune = new("mt-docker-prune",
		"M14 3l7 7 M17.5 6.5L11 13 M11 13l-2.5-2.5L3 16l5 5 5.5-5.5z M6 18.5l2-2");
}
