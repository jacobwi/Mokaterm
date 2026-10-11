namespace Mokaterm.Modules.Ssh.Admin.Network;

/// <summary>One address on an interface.</summary>
internal sealed record NetAddress(string Family, string Address, int PrefixLength, string Scope)
{
	public bool IsIpv6 => Family == "inet6";

	public string Cidr => $"{Address}/{PrefixLength}";
}
