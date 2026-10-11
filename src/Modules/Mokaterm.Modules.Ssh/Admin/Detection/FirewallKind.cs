namespace Mokaterm.Modules.Ssh.Admin.Detection;

internal enum FirewallKind
{
	None,
	Ufw,
	Firewalld,
	Nftables,
	Iptables,
}
