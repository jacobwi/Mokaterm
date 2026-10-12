using System.Net;
using System.Net.Sockets;
using System.Numerics;

namespace Mokaterm.Plugins.Sample.Subnets;

/// <summary>One IPv4 or IPv6 prefix worked out: where it starts and ends, which addresses hosts can take, and how many.</summary>
internal sealed record SubnetInfo
{
	public required IPAddress Address { get; init; }

	public required int PrefixLength { get; init; }

	/// <summary>The first address of the prefix, with every host bit clear.</summary>
	public required IPAddress Network { get; init; }

	/// <summary>The prefix as a mask: <c>255.255.255.0</c>, or <c>ffff:ffff:ffff:ffff::</c> for IPv6.</summary>
	public required IPAddress Mask { get; init; }

	/// <summary>The host bits as a mask, as access lists write it: <c>0.0.0.255</c>. IPv4 only.</summary>
	public IPAddress? Wildcard { get; init; }

	/// <summary>The last address, every host bit set, which IPv4 sends to all hosts. Null for IPv6, which has none, and for /31 and /32, which use it as a host.</summary>
	public IPAddress? Broadcast { get; init; }

	public required IPAddress FirstHost { get; init; }

	public required IPAddress LastHost { get; init; }

	/// <summary>Every address in the prefix.</summary>
	public required BigInteger AddressCount { get; init; }

	/// <summary>The addresses a host can take: IPv4 leaves out the network and broadcast addresses below /31.</summary>
	public required BigInteger HostCount { get; init; }

	public bool IsIPv6 => Address.AddressFamily == AddressFamily.InterNetworkV6;

	/// <summary>The prefix the way it is written: <c>192.168.1.0/24</c>.</summary>
	public string Cidr => $"{Network}/{PrefixLength}";
}
