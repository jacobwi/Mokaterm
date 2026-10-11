using Moka.Red.Core.Icons;

namespace Mokaterm.Modules.Ssh;

/// <summary>Icons this module needs beyond the shared set, drawn the same way: single stroked path on a 24x24 grid.</summary>
internal static class SshIcons
{
	/// <summary>A pipe with an arrow through it, for port forwarding.</summary>
	public static readonly MokaIconDefinition Tunnel = new("mt-tunnel",
		"M2 12a10 6 0 0 1 20 0v6 M2 12v6 M7 12a5 3 0 0 1 10 0 M7 18v-6 M17 18v-6 M2 15h20");

	/// <summary>A pulse line, for the system overview.</summary>
	public static readonly MokaIconDefinition Pulse = new("mt-pulse", "M3 12h4l3-8 4 16 3-8h4");

	/// <summary>A gear, for services.</summary>
	public static readonly MokaIconDefinition Gear = new("mt-gear",
		"M12 8a4 4 0 1 0 0 8a4 4 0 1 0 0-8 M12 2v3 M12 19v3 M2 12h3 M19 12h3 M4.9 4.9l2.1 2.1 M17 17l2.1 2.1 M4.9 19.1L7 17 M17 7l2.1-2.1");

	/// <summary>A box, for packages.</summary>
	public static readonly MokaIconDefinition Package = new("mt-package", "M3 7l9-4 9 4v10l-9 4-9-4z M3 7l9 4 9-4 M12 11v10");

	/// <summary>A calendar, for scheduled jobs.</summary>
	public static readonly MokaIconDefinition Calendar = new("mt-calendar", "M4 5h16v16H4z M4 9h16 M8 3v4 M16 3v4 M8 13h2 M14 13h2 M8 17h2");

	/// <summary>A clock face, for time and date.</summary>
	public static readonly MokaIconDefinition Clock = new("mt-clock", "M12 3a9 9 0 1 0 0 18a9 9 0 1 0 0-18 M12 7v5l3 2");

	/// <summary>Three boxes joined to a fourth, for network interfaces.</summary>
	public static readonly MokaIconDefinition Network = new("mt-network", "M9 3h6v5H9z M2 16h6v5H2z M16 16h6v5h-6z M12 8v4 M5 16v-4h14v4");

	/// <summary>A cylinder, for disks.</summary>
	public static readonly MokaIconDefinition Disk = new("mt-disk", "M3 6a9 3 0 0 1 18 0v12a9 3 0 0 1-18 0z M3 6a9 3 0 0 0 18 0 M3 12a9 3 0 0 0 18 0");

	/// <summary>A chip, for processes.</summary>
	public static readonly MokaIconDefinition Chip = new("mt-chip", "M7 7h10v10H7z M9 2v5 M15 2v5 M9 17v5 M15 17v5 M2 9h5 M2 15h5 M17 9h5 M17 15h5");

	/// <summary>A page with lines, for logs.</summary>
	public static readonly MokaIconDefinition Log = new("mt-log", "M5 3h10l4 4v14H5z M15 3v4h4 M8 11h8 M8 15h8 M8 19h5");

	/// <summary>Two people, for users and groups.</summary>
	public static readonly MokaIconDefinition Users = new("mt-users",
		"M9 11a4 4 0 1 0 0-8a4 4 0 1 0 0 8 M2 21v-2a5 5 0 0 1 5-5h4a5 5 0 0 1 5 5v2 M16 3.5a4 4 0 0 1 0 7 M22 21v-2a5 5 0 0 0-3-4.6");
}
