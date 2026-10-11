namespace Mokaterm.Modules.Ssh.Admin.Firewall;

/// <summary>What the firewall page shows: on or off, the defaults, the rules, and the tool's own text for the rest.</summary>
internal sealed record FirewallStatus
{
	/// <summary>True when it filters, false when it is off, null when the output did not say.</summary>
	public bool? Active { get; init; }

	/// <summary>Lines such as the default policies, the zone, logging.</summary>
	public IReadOnlyList<(string Key, string Value)> Summary { get; init; } = [];

	public IReadOnlyList<FirewallRule> Rules { get; init; } = [];

	/// <summary>The output as printed, for nftables and iptables, which are shown and not interpreted.</summary>
	public string Raw { get; init; } = "";
}
