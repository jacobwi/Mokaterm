using System.Globalization;
using System.Text;
using Mokaterm.DevHost.Demo.FileSystem;

namespace Mokaterm.DevHost.Demo;

/// <summary>
/// What an Ubuntu server would print for the SSH module's system view, so its pages can be looked at here. The view
/// tags each script on its first line; reads get canned output in the shape the real tools print, and anything else
/// (every change, since the demo never runs as root) gets nothing and exit 127.
/// </summary>
internal static partial class DemoSystemOutput
{
	/// <summary>The tag the system view puts on the first line of every script it sends.</summary>
	private const string Tag = "# mokaterm system view: ";

	private static readonly string[] Packages =
	[
		"adduser\t3.137ubuntu1\tall", "apt\t2.7.14build2\tamd64", "bash\t5.2.21-2ubuntu4\tamd64", "ca-certificates\t20240203\tall",
		"coreutils\t9.4-3ubuntu6\tamd64", "cron\t3.0pl1-184ubuntu2\tamd64", "curl\t8.5.0-2ubuntu10.4\tamd64", "dpkg\t1.22.6ubuntu6.1\tamd64",
		"git\t1:2.43.0-1ubuntu7.1\tamd64", "htop\t3.3.0-4build1\tamd64", "iproute2\t6.1.0-1ubuntu6\tamd64", "libc6\t2.39-0ubuntu8.3\tamd64",
		"libssl3t64\t3.0.13-0ubuntu3.3\tamd64", "linux-image-6.8.0-45-generic\t6.8.0-45.45\tamd64", "nginx\t1.24.0-2ubuntu7.1\tamd64",
		"openssh-server\t1:9.6p1-3ubuntu13.5\tamd64", "postgresql-16\t16.4-0ubuntu0.24.04.2\tamd64", "python3\t3.12.3-0ubuntu2\tamd64",
		"sudo\t1.9.15p5-3ubuntu5\tamd64", "systemd\t255.4-1ubuntu8.4\tamd64", "tzdata\t2024a-3ubuntu1.1\tall", "ufw\t0.36.2-6\tall",
		"vim\t2:9.1.0016-1ubuntu7.2\tamd64",
	];

	/// <summary>The script name from a command line the system view sent, or null for any other command.</summary>
	public static string? ScriptName(string command)
	{
		int start = command.IndexOf(Tag, StringComparison.Ordinal);
		if (start < 0)
		{
			return null;
		}

		start += Tag.Length;
		int end = command.IndexOf('\n', start);
		return end < 0 ? null : command[start..end];
	}

	/// <summary>The stdout a read prints after its marker, or null for a script the demo does not answer.</summary>
	public static string? For(string script, DemoAccount account, string hostName, DateTimeOffset now)
	{
		if (ForAdmin(script, account, hostName, now) is { } admin)
		{
			return admin;
		}

		long epoch = now.ToUnixTimeSeconds();
		return script switch
		{
			"detect" => Detect(account, hostName),
			"survey" => DemoSurveyOutput.For(account, hostName, now),
			"overview.read" => DemoMonitorOutput.Sample(now) + string.Create(CultureInfo.InvariantCulture, $"""
				@@mt:now
				{epoch}
				@@mt:cpu
				model name	: AMD EPYC 7763 64-Core Processor
				@@mt:cores
				4
				{Df}
				"""),
			"disks.read" => $$"""
				@@mt:lsblk
				{"blockdevices": [
				   {"name":"vda", "type":"disk", "size":85899345920, "fstype":null, "mountpoint":null, "label":null, "model":null, "ro":false, "rm":false,
				      "children": [
				         {"name":"vda1", "type":"part", "size":84825604096, "fstype":"ext4", "mountpoint":"/", "label":"cloudimg-rootfs", "model":null, "ro":false, "rm":false},
				         {"name":"vda15", "type":"part", "size":1073741824, "fstype":"vfat", "mountpoint":"/boot/efi", "label":"UEFI", "model":null, "ro":false, "rm":false}
				      ]
				   },
				   {"name":"vdb", "type":"disk", "size":214748364800, "fstype":"xfs", "mountpoint":"/srv/data", "label":"data", "model":null, "ro":false, "rm":false},
				   {"name":"sda", "type":"disk", "size":4000787030016, "fstype":null, "mountpoint":null, "label":null, "model":"WDC WD40EFRX-68N32N0", "ro":false, "rm":false,
				      "children": [
				         {"name":"sda1", "type":"part", "size":4000785104896, "fstype":"ext4", "mountpoint":null, "label":"archive", "model":null, "ro":false, "rm":false}
				      ]
				   }
				]}
				@@mt:partitions
				{{Df}}
				""",
			"services.list" => """
				@@mt:units
				cron.service                 loaded    active   running Regular background program processing daemon
				nginx.service                loaded    active   running A high performance web server and a reverse proxy server
				postgresql@16-main.service   loaded    active   running PostgreSQL Cluster 16-main
				ssh.service                  loaded    active   running OpenBSD Secure Shell server
				systemd-journald.service     loaded    active   running Journal Service
				systemd-resolved.service     loaded    active   running Network Name Resolution
				systemd-tmpfiles-setup.service loaded  active   exited  Create System Files and Directories
				ufw.service                  loaded    active   exited  Uncomplicated firewall
				apt-daily.service            loaded    inactive dead    Daily apt download activities
				backup-agent.service         loaded    failed   failed  Nightly backup agent
				@@mt:files
				apt-daily.service                          static          -
				backup-agent.service                       enabled         enabled
				cron.service                               enabled         enabled
				nginx.service                              enabled         enabled
				postgresql@.service                        indirect        enabled
				redis-server.service                       disabled        enabled
				ssh.service                                enabled         enabled
				ufw.service                                enabled         enabled
				""",
			"services.status" => """
				* nginx.service - A high performance web server and a reverse proxy server
				     Loaded: loaded (/usr/lib/systemd/system/nginx.service; enabled; preset: enabled)
				     Active: active (running) since Thu 2026-09-29 08:12:44 UTC; 1 week 5 days ago
				   Main PID: 1123 (nginx)
				      Tasks: 5 (limit: 9387)
				     Memory: 9.8M (peak: 12.1M)
				Hint: You are currently not seeing messages from other users and the system.
				      Users in groups 'adm', 'systemd-journal' can see all messages.
				""",
			"packages.installed" => string.Join('\n', Packages.Select(line => line + "\tinstall ok installed")) + "\n",
			"packages.updates" => """
				Listing...
				curl/noble-updates 8.5.0-2ubuntu10.5 amd64 [upgradable from: 8.5.0-2ubuntu10.4]
				libssl3t64/noble-security 3.0.13-0ubuntu3.4 amd64 [upgradable from: 3.0.13-0ubuntu3.3]
				linux-image-generic/noble-updates 6.8.0-47.47 amd64 [upgradable from: 6.8.0-45.45]
				""",
			"cron.read" => account.IsRoot
				? "# m h  dom mon dow   command\n15 4 * * * /usr/local/sbin/rotate-backups\n"
				: "# m h  dom mon dow   command\nMAILTO=ops@example.com\n0 3 * * * /home/" + account.Name + "/bin/backup.sh >/dev/null 2>&1\n*/15 * * * 1-5 /usr/bin/curl -fsS https://health.example.com/ping\n@reboot /home/" + account.Name + "/bin/start-agent\n",
			"cron.system" => """
				@@mt:file /etc/crontab
				SHELL=/bin/sh
				PATH=/usr/local/sbin:/usr/local/bin:/sbin:/bin:/usr/sbin:/usr/bin
				17 *	* * *	root	cd / && run-parts --report /etc/cron.hourly
				25 6	* * *	root	test -x /usr/sbin/anacron || { cd / && run-parts --report /etc/cron.daily; }

				@@mt:file /etc/cron.d/e2scrub_all
				30 3 * * 0 root test -e /run/systemd/system || SERVICE_MODE=1 /usr/lib/x86_64-linux-gnu/e2fsprogs/e2scrub_all_cron

				@@mt:unreadable /etc/cron.d/certbot
				@@mt:folder daily
				apt-compat
				dpkg
				logrotate
				@@mt:folder hourly
				""",
			"cron.timers" => """
				Id=apt-daily.timer
				Description=Daily apt download activities
				ActiveState=active
				NextElapseUSecRealtime=Mon 2026-10-12 06:41:12 UTC
				LastTriggerUSec=Sun 2026-10-11 06:12:01 UTC
				Unit=apt-daily.service
				TimersCalendar={ OnCalendar=*-*-* 06,18:00:00 ; next_elapse=Mon 2026-10-12 06:00:00 UTC }
				TimersMonotonic=

				Id=logrotate.timer
				Description=Daily rotation of log files
				ActiveState=active
				NextElapseUSecRealtime=Mon 2026-10-12 00:00:00 UTC
				LastTriggerUSec=Sun 2026-10-11 00:00:01 UTC
				Unit=logrotate.service
				TimersCalendar={ OnCalendar=*-*-* 00:00:00 ; next_elapse=Mon 2026-10-12 00:00:00 UTC }
				TimersMonotonic=
				""",
			"time.read" => string.Create(CultureInfo.InvariantCulture, $"""
				@@mt:date
				{now.UtcDateTime:yyyy-MM-dd HH:mm:ss} UTC (+0000)
				@@mt:epoch
				{epoch}
				@@mt:show
				Timezone=Etc/UTC
				LocalRTC=no
				CanNTP=yes
				NTP=yes
				NTPSynchronized=yes
				"""),
			"time.zones" => "Africa/Abidjan\nAmerica/New_York\nAmerica/Sao_Paulo\nAsia/Tokyo\nAustralia/Sydney\nEtc/UTC\nEurope/Berlin\nEurope/London\nUTC\n",
			"hostname.read" => $"""
				@@mt:current
				{hostName}
				@@mt:file
				{hostName}
				@@mt:ctl
				 Static hostname: {hostName}
				       Icon name: computer-vm
				         Chassis: vm
				  Virtualization: kvm
				Operating System: Ubuntu 24.04.1 LTS
				          Kernel: Linux 6.8.0-45-generic
				    Architecture: x86-64
				""",
			"network.read" => """
				@@mt:addr-json
				[{"ifindex":1,"ifname":"lo","flags":["LOOPBACK","UP","LOWER_UP"],"mtu":65536,"operstate":"UNKNOWN","link_type":"loopback","address":"00:00:00:00:00:00","addr_info":[{"family":"inet","local":"127.0.0.1","prefixlen":8,"scope":"host"},{"family":"inet6","local":"::1","prefixlen":128,"scope":"host"}]},{"ifindex":2,"ifname":"eth0","flags":["BROADCAST","MULTICAST","UP","LOWER_UP"],"mtu":1500,"operstate":"UP","link_type":"ether","address":"52:54:00:12:34:56","addr_info":[{"family":"inet","local":"10.0.0.5","prefixlen":24,"scope":"global"},{"family":"inet6","local":"fe80::5054:ff:fe12:3456","prefixlen":64,"scope":"link"}]},{"ifindex":3,"ifname":"eth1","flags":["BROADCAST","MULTICAST"],"mtu":9000,"operstate":"DOWN","link_type":"ether","address":"52:54:00:ab:cd:ef","addr_info":[]}]
				@@mt:route4-json
				[{"dst":"default","gateway":"10.0.0.1","dev":"eth0","protocol":"dhcp","prefsrc":"10.0.0.5","metric":100},{"dst":"10.0.0.0/24","dev":"eth0","protocol":"kernel","scope":"link","prefsrc":"10.0.0.5","metric":100}]
				@@mt:route6-json
				[{"dst":"fe80::/64","dev":"eth0","protocol":"kernel","metric":256}]
				@@mt:resolvectl
				Global:
				Link 2 (eth0): 10.0.0.1
				@@mt:resolvectl-domain
				Link 2 (eth0): lan
				@@mt:resolv
				nameserver 127.0.0.53
				search lan
				@@mt:ss
				Netid State  Recv-Q Send-Q Local Address:Port  Peer Address:PortProcess
				udp   UNCONN 0      0      127.0.0.53%lo:53         0.0.0.0:*
				tcp   LISTEN 0      4096         0.0.0.0:22         0.0.0.0:*
				tcp   LISTEN 0      511          0.0.0.0:80         0.0.0.0:*
				tcp   LISTEN 0      200        127.0.0.1:5432       0.0.0.0:*
				tcp   LISTEN 0      4096            [::]:22            [::]:*
				""",
			"accounts.read" => $"""
				@@mt:passwd
				root:x:0:0:root:/root:/bin/bash
				daemon:x:1:1:daemon:/usr/sbin:/usr/sbin/nologin
				www-data:x:33:33:www-data:/var/www:/usr/sbin/nologin
				postgres:x:113:121:PostgreSQL administrator,,,:/var/lib/postgresql:/bin/bash
				{account.Name}:x:1000:1000:{account.Name},,,:{account.Home}:/bin/bash
				deploy:x:1001:1001:Deploy bot:/home/deploy:/bin/bash
				nobody:x:65534:65534:nobody:/nonexistent:/usr/sbin/nologin
				@@mt:group
				root:x:0:
				sudo:x:27:{account.Name}
				www-data:x:33:deploy
				{account.Name}:x:1000:
				deploy:x:1001:
				docker:x:999:{account.Name},deploy
				@@mt:shells
				/bin/sh
				/bin/bash
				/usr/bin/bash
				@@mt:logindefs
				UID_MIN			 1000
				""",
			"logs.files" => "/var/log/alternatives.log\n/var/log/apt/history.log\n/var/log/auth.log\n/var/log/nginx/access.log\n/var/log/nginx/error.log\n/var/log/syslog\n",
			_ => null,
		};
	}

	private const string Df = """
		@@mt:df
		Filesystem     1024-blocks     Used Available Capacity Mounted on
		/dev/vda1         81971444 38213332  43741728      47% /
		tmpfs               803788     1104    802684       1% /run
		/dev/vda15          106832     6186    100646       6% /boot/efi
		/dev/vdb         209612800 191232000  18380800      92% /srv/data
		@@mt:mounts
		/dev/vda1 / ext4 rw,relatime 0 0
		tmpfs /run tmpfs rw,nosuid,nodev 0 0
		/dev/vda15 /boot/efi vfat rw,relatime 0 0
		/dev/vdb /srv/data xfs rw,relatime 0 0
		""";

	private static string Detect(DemoAccount account, string hostName)
	{
		StringBuilder text = new();
		text.Append("@@mt:facts\n")
			.Append("kernel_name=Linux\nkernel=Linux 6.8.0-45-generic x86_64\n")
			.Append("hostname=").Append(hostName).Append('\n')
			.Append("uid=").Append(account.IsRoot ? "0" : "1000").Append('\n')
			.Append("user=").Append(account.Name).Append('\n')
			.Append("pid1=systemd\ninit_link=/lib/systemd/systemd\nssh=203.0.113.9 51234 10.0.0.5 22\nufw=yes\n")
			.Append("@@mt:commands\n");
		foreach (string command in (string[])["systemctl", "journalctl", "timedatectl", "hostnamectl", "resolvectl", "service", "apt-get", "dpkg-query", "ufw", "nft", "iptables", "ip", "ss", "lsblk", "df", "ps", "kill", "crontab", "sudo", "getent", "useradd", "usermod", "userdel", "chpasswd", "passwd", "smartctl"])
		{
			text.Append(command).Append('\n');
		}

		text.Append("@@mt:paths\n/run/systemd/system\n/etc/localtime\n/usr/share/zoneinfo\n/etc/crontab\n/etc/cron.d\n/var/log/journal\n/etc/ufw/ufw.conf\n/etc/login.defs\n")
			.Append("@@mt:units\nfirewalld=inactive\nufw=active\nnftables=inactive\n")
			.Append("@@mt:os-release\nPRETTY_NAME=\"Ubuntu 24.04.1 LTS\"\nNAME=\"Ubuntu\"\nVERSION_ID=\"24.04\"\nID=ubuntu\nID_LIKE=debian\n")
			.Append("@@mt:sw_vers\n");
		return text.ToString();
	}
}
