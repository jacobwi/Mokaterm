namespace Mokaterm.Modules.Ssh.Admin.Network;

/// <summary>One network interface as <c>ip addr</c> describes it.</summary>
internal sealed record NetInterface
{
	public required string Name { get; init; }

	/// <summary>The kernel's operational state: <c>UP</c>, <c>DOWN</c>, <c>UNKNOWN</c> (loopback and many tunnels), <c>DORMANT</c>.</summary>
	public string State { get; init; } = "";

	public IReadOnlyList<string> Flags { get; init; } = [];

	public int? Mtu { get; init; }

	/// <summary>The link-layer address, empty for interfaces without one.</summary>
	public string Mac { get; init; } = "";

	/// <summary><c>ether</c>, <c>loopback</c>, <c>none</c> and so on.</summary>
	public string LinkType { get; init; } = "";

	public IReadOnlyList<NetAddress> Addresses { get; init; } = [];

	/// <summary>Administratively up: the UP flag, whatever the cable says.</summary>
	public bool IsUp => Flags.Contains("UP");

	public bool IsLoopback => Flags.Contains("LOOPBACK") || LinkType == "loopback";
}
