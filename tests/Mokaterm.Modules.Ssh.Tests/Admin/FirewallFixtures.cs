namespace Mokaterm.Modules.Ssh.Tests.Admin;

/// <summary>What ufw and firewalld print for the firewall page.</summary>
internal static class FirewallFixtures
{
	public const string UfwActive = """
		Status: active
		Logging: on (low)
		Default: deny (incoming), allow (outgoing), disabled (routed)
		New profiles: skip

		To                         Action      From
		--                         ------      ----
		22/tcp                     ALLOW IN    Anywhere
		80,443/tcp                 ALLOW IN    Anywhere
		3306                       DENY IN     10.0.0.0/8
		22/tcp (v6)                ALLOW IN    Anywhere (v6)

		""";

	public const string UfwInactive = """
		Status: inactive

		""";

	public const string FirewalldRunning = """
		@@mt:state
		running
		@@mt:zone
		public
		@@mt:all
		public (active)
		  target: default
		  icmp-block-inversion: no
		  interfaces: eth0
		  sources:
		  services: cockpit dhcpv6-client ssh
		  ports: 8080/tcp 5000-5010/udp
		  protocols:
		  forward: yes
		  masquerade: no
		  forward-ports:
		  source-ports:
		  icmp-blocks:
		  rich rules:
			rule family="ipv4" source address="192.0.2.0/24" port port="9000" protocol="tcp" reject

		""";

	public const string FirewalldStopped = """
		@@mt:state
		not running
		@@mt:zone
		@@mt:all

		""";
}
