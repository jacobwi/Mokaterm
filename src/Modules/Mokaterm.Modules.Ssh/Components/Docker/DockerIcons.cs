using Moka.Red.Core.Icons;
using Moka.Red.Icons;

namespace Mokaterm.Modules.Ssh.Components.Docker;

/// <summary>The Docker manager's icons: Moka's where it has the drawing, the rest single stroked paths on a 24x24 grid.</summary>
internal static class DockerIcons
{
	public static readonly MokaIconDefinition Container = MokaIcons.Content.Cube;

	public static readonly MokaIconDefinition Compose = MokaIcons.Content.Layers;

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

	public static readonly MokaIconDefinition Stop = MokaIcons.Action.Stop;

	public static readonly MokaIconDefinition StopRestarting = new("mt-docker-stop-restarting",
		"M20 12a8 8 0 1 1-2.34-5.66 M20 3.5V8h-4.5 M4 4l16 16");

	public static readonly MokaIconDefinition Kill = MokaIcons.Content.Zap;

	public static readonly MokaIconDefinition Stats = MokaIcons.Content.BarChart;

	public static readonly MokaIconDefinition Update = MokaIcons.Navigation.ArrowUpCircle;

	/// <summary>A warehouse with its door, for registries.</summary>
	public static readonly MokaIconDefinition Registry = new("mt-docker-registry",
		"M3 9.5L12 4l9 5.5V20H3z M8 20v-7h8v7 M8 16.5h8");

	public static readonly MokaIconDefinition Build = MokaIcons.Content.Hammer;

	public static readonly MokaIconDefinition Push = MokaIcons.Navigation.ArrowUpToLine;

	public static readonly MokaIconDefinition Tag = MokaIcons.Content.Tag;

	public static readonly MokaIconDefinition Prune = MokaIcons.Action.Broom;
}
