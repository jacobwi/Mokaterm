using Moka.Red.Core.Icons;
using Moka.Red.Icons;

namespace Mokaterm.Modules.Ssh;

/// <summary>Icons this module needs beyond the shared set: Moka's where it has the drawing, the rest single stroked paths on a 24x24 grid.</summary>
internal static class SshIcons
{
	public static readonly MokaIconDefinition Tunnel = MokaIcons.Content.Tunnel;

	public static readonly MokaIconDefinition Pulse = MokaIcons.Status.Activity;

	/// <summary>A gear, for services.</summary>
	public static readonly MokaIconDefinition Gear = new("mt-gear",
		"M12 8a4 4 0 1 0 0 8a4 4 0 1 0 0-8 M12 2v3 M12 19v3 M2 12h3 M19 12h3 M4.9 4.9l2.1 2.1 M17 17l2.1 2.1 M4.9 19.1L7 17 M17 7l2.1-2.1");

	public static readonly MokaIconDefinition Package = MokaIcons.Content.Cube;

	/// <summary>A calendar, for scheduled jobs.</summary>
	public static readonly MokaIconDefinition Calendar = new("mt-calendar", "M4 5h16v16H4z M4 9h16 M8 3v4 M16 3v4 M8 13h2 M14 13h2 M8 17h2");

	public static readonly MokaIconDefinition Clock = MokaIcons.Status.Clock;

	public static readonly MokaIconDefinition Network = MokaIcons.Content.Network;

	public static readonly MokaIconDefinition Disk = MokaIcons.File.Database;

	/// <summary>A chip, for processes.</summary>
	public static readonly MokaIconDefinition Chip = new("mt-chip", "M7 7h10v10H7z M9 2v5 M15 2v5 M9 17v5 M15 17v5 M2 9h5 M2 15h5 M17 9h5 M17 15h5");

	/// <summary>A page with lines, for logs.</summary>
	public static readonly MokaIconDefinition Log = new("mt-log", "M5 3h10l4 4v14H5z M15 3v4h4 M8 11h8 M8 15h8 M8 19h5");

	public static readonly MokaIconDefinition Users = MokaIcons.Content.Users;
}
