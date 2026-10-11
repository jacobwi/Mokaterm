namespace Mokaterm.Modules.Ssh.Tests.Admin;

/// <summary>Network page output: iproute2 with JSON, BusyBox's plain ip, ss and netstat, resolvectl and resolv.conf.</summary>
internal static class NetworkFixtures
{
	public const string IpJson = """
		@@mt:addr-json
		[{"ifindex":1,"ifname":"lo","flags":["LOOPBACK","UP","LOWER_UP"],"mtu":65536,"qdisc":"noqueue","operstate":"UNKNOWN","group":"default","txqlen":1000,"link_type":"loopback","address":"00:00:00:00:00:00","broadcast":"00:00:00:00:00:00","addr_info":[{"family":"inet","local":"127.0.0.1","prefixlen":8,"scope":"host","label":"lo","valid_life_time":4294967295,"preferred_life_time":4294967295},{"family":"inet6","local":"::1","prefixlen":128,"scope":"host","valid_life_time":4294967295,"preferred_life_time":4294967295}]},{"ifindex":2,"ifname":"eth0","flags":["BROADCAST","MULTICAST","UP","LOWER_UP"],"mtu":1500,"qdisc":"fq_codel","operstate":"UP","group":"default","txqlen":1000,"link_type":"ether","address":"52:54:00:12:34:56","broadcast":"ff:ff:ff:ff:ff:ff","addr_info":[{"family":"inet","local":"10.0.0.5","prefixlen":24,"broadcast":"10.0.0.255","scope":"global","dynamic":true,"label":"eth0","valid_life_time":85000,"preferred_life_time":85000},{"family":"inet6","local":"fe80::5054:ff:fe12:3456","prefixlen":64,"scope":"link","valid_life_time":4294967295,"preferred_life_time":4294967295}]},{"ifindex":3,"ifname":"eth1","flags":["BROADCAST","MULTICAST"],"mtu":9000,"qdisc":"noop","operstate":"DOWN","group":"default","txqlen":1000,"link_type":"ether","address":"52:54:00:ab:cd:ef","broadcast":"ff:ff:ff:ff:ff:ff","addr_info":[{"family":"inet","local":"192.168.50.2","prefixlen":24,"scope":"global","label":"eth1","valid_life_time":4294967295,"preferred_life_time":4294967295}]}]
		@@mt:route4-json
		[{"dst":"default","gateway":"10.0.0.1","dev":"eth0","protocol":"dhcp","prefsrc":"10.0.0.5","metric":100,"flags":[]},{"dst":"10.0.0.0/24","dev":"eth0","protocol":"kernel","scope":"link","prefsrc":"10.0.0.5","metric":100,"flags":[]}]
		@@mt:route6-json
		[{"dst":"fe80::/64","dev":"eth0","protocol":"kernel","metric":256,"flags":[],"pref":"medium"}]
		@@mt:resolvectl
		Global:
		Link 2 (eth0): 10.0.0.1 fe80::1%eth0
		Link 3 (eth1):
		@@mt:resolvectl-domain
		Global:
		Link 2 (eth0): lan
		@@mt:resolv
		# This is /run/systemd/resolve/stub-resolv.conf managed by man:systemd-resolved(8).
		nameserver 127.0.0.53
		options edns0 trust-ad
		search lan
		@@mt:ss
		Netid State  Recv-Q Send-Q Local Address:Port  Peer Address:PortProcess
		udp   UNCONN 0      0      127.0.0.53%lo:53         0.0.0.0:*    users:(("systemd-resolve",pid=612,fd=13))
		tcp   LISTEN 0      4096         0.0.0.0:22         0.0.0.0:*    users:(("sshd",pid=801,fd=3))
		tcp   LISTEN 0      511          0.0.0.0:80         0.0.0.0:*
		tcp   LISTEN 0      4096            [::]:22            [::]:*    users:(("sshd",pid=801,fd=4))

		""";

	/// <summary>BusyBox: no JSON, no ss.</summary>
	public const string BusyBoxText = """
		@@mt:addr-json
		@@mt:addr-text
		1: lo: <LOOPBACK,UP,LOWER_UP> mtu 65536 qdisc noqueue state UNKNOWN qlen 1000
		    link/loopback 00:00:00:00:00:00 brd 00:00:00:00:00:00
		    inet 127.0.0.1/8 scope host lo
		       valid_lft forever preferred_lft forever
		2: eth0@if7: <BROADCAST,MULTICAST,UP,LOWER_UP,M-DOWN> mtu 1500 qdisc noqueue state UP
		    link/ether 02:42:ac:11:00:02 brd ff:ff:ff:ff:ff:ff
		    inet 172.17.0.2/16 brd 172.17.255.255 scope global eth0
		       valid_lft forever preferred_lft forever
		@@mt:route4-json
		@@mt:route-text
		default via 172.17.0.1 dev eth0
		172.17.0.0/16 dev eth0 scope link  src 172.17.0.2
		@@mt:resolv
		nameserver 192.168.65.7
		search example.internal
		@@mt:netstat
		Active Internet connections (only servers)
		Proto Recv-Q Send-Q Local Address           Foreign Address         State       PID/Program name
		tcp        0      0 0.0.0.0:22              0.0.0.0:*               LISTEN      1/sshd
		tcp        0      0 :::22                   :::*                    LISTEN      1/sshd
		udp        0      0 0.0.0.0:68              0.0.0.0:*                           12/udhcpc

		""";
}
