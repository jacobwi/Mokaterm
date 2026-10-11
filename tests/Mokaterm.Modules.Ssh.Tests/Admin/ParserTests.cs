using Mokaterm.Modules.Ssh.Admin;
using Mokaterm.Modules.Ssh.Admin.Accounts;
using Mokaterm.Modules.Ssh.Admin.Clock;
using Mokaterm.Modules.Ssh.Admin.Cron;
using Mokaterm.Modules.Ssh.Admin.Detection;
using Mokaterm.Modules.Ssh.Admin.Firewall;
using Mokaterm.Modules.Ssh.Admin.Naming;
using Mokaterm.Modules.Ssh.Admin.Network;
using Mokaterm.Modules.Ssh.Admin.Overview;
using Mokaterm.Modules.Ssh.Admin.Packages;
using Mokaterm.Modules.Ssh.Admin.Processes;
using Mokaterm.Modules.Ssh.Admin.Services;
using Mokaterm.Modules.Ssh.Admin.Storage;

namespace Mokaterm.Modules.Ssh.Tests.Admin;

public sealed class ParserTests
{
	[Fact]
	public void Systemd_JoinsUnitsWithTheirUnitFiles()
	{
		const string Output = """
			@@mt:units
			cron.service                 loaded    active   running Regular background program processing daemon
			nginx.service                loaded    failed   failed  A high performance web server
			apt-daily.service            loaded    inactive dead    Daily apt download activities
			systemd-tmpfiles-setup.service loaded  active   exited  Create Volatile Files and Directories
			ghost.service                not-found inactive dead    ghost.service
			getty@tty1.service           loaded    active   running Getty on tty1
			@@mt:files
			apt-daily.service                          static          -
			cron.service                               enabled         enabled
			nginx.service                              disabled        enabled
			getty@.service                             enabled         enabled
			unused.service                             disabled        disabled
			masked.service                             masked          enabled
			""";

		IReadOnlyList<ServiceUnit> units = ServiceListParser.ParseSystemd(Output);

		ServiceUnit cron = units.Single(unit => unit.Name == "cron.service");
		Assert.Equal(ServiceState.Running, cron.State);
		Assert.Equal("active (running)", cron.StateText);
		Assert.True(cron.Enabled);
		Assert.Equal("Regular background program processing daemon", cron.Description);

		ServiceUnit nginx = units.Single(unit => unit.Name == "nginx.service");
		Assert.Equal(ServiceState.Failed, nginx.State);
		Assert.False(nginx.Enabled);

		Assert.Equal(ServiceState.Stopped, units.Single(unit => unit.Name == "apt-daily.service").State);
		Assert.Null(units.Single(unit => unit.Name == "apt-daily.service").Enabled);
		Assert.Equal(ServiceState.Exited, units.Single(unit => unit.Name == "systemd-tmpfiles-setup.service").State);
		Assert.StartsWith("Not found", units.Single(unit => unit.Name == "ghost.service").Description, StringComparison.Ordinal);

		// Unit files that never loaded still get a row; templates do not, their instances do.
		Assert.Equal(ServiceState.Stopped, units.Single(unit => unit.Name == "unused.service").State);
		Assert.False(units.Single(unit => unit.Name == "masked.service").Enabled);
		Assert.DoesNotContain(units, unit => unit.Name == "getty@.service");
		Assert.Contains(units, unit => unit.Name == "getty@tty1.service");
	}

	[Fact]
	public void OpenRc_JoinsStatusAndRunlevels()
	{
		const string Output = """
			@@mt:status
			 acpid                                                      [  stopped  ]
			 crond                                                      [  started  ]
			 sshd                                                       [  started  ]
			 networking                                                 [  crashed  ]
			@@mt:levels
			              acpid |
			              crond |      default
			         networking | boot
			               sshd |      default
			            chronyd |      default
			""";

		IReadOnlyList<ServiceUnit> units = ServiceListParser.ParseOpenRc(Output);

		Assert.Equal(["acpid", "chronyd", "crond", "networking", "sshd"], units.Select(unit => unit.Name));
		Assert.Equal(ServiceState.Stopped, units[0].State);
		Assert.False(units[0].Enabled);
		Assert.Equal(ServiceState.Stopped, units[1].State);
		Assert.True(units[1].Enabled);
		Assert.Equal(ServiceState.Failed, units[3].State);
		Assert.Equal("boot", units[3].EnabledText);
		Assert.Equal(ServiceState.Running, units[4].State);
	}

	[Fact]
	public void SysV_ReadsDebianAndRedHatStatusLines()
	{
		const string Output = """
			@@mt:scripts
			README
			cron
			ssh
			httpd
			rc
			skeleton
			@@mt:status
			 [ + ]  cron
			 [ - ]  ssh
			httpd (pid  1234) is running...
			@@mt:links
			README
			S01cron
			K01ssh
			S20httpd
			""";

		IReadOnlyList<ServiceUnit> units = ServiceListParser.ParseSysV(Output);

		Assert.Equal(["cron", "httpd", "ssh"], units.Select(unit => unit.Name));
		Assert.Equal(ServiceState.Running, units[0].State);
		Assert.True(units[0].Enabled);
		Assert.Equal(ServiceState.Running, units[1].State);
		Assert.Equal(ServiceState.Stopped, units[2].State);
		Assert.False(units[2].Enabled);
	}

	[Fact]
	public void Packages_Installed_PerManager()
	{
		IReadOnlyList<InstalledPackage> dpkg = PackageListParser.ParseDpkg(
			"bash\t5.2.15-2+b7\tamd64\tinstall ok installed\nold\t1.0\tall\tdeinstall ok config-files\nlibc6\t2.36-9\tamd64\tinstall ok installed\n");
		Assert.Equal([new InstalledPackage("bash", "5.2.15-2+b7", "amd64"), new InstalledPackage("libc6", "2.36-9", "amd64")], dpkg);

		IReadOnlyList<InstalledPackage> rpm = PackageListParser.ParseRpm("zlib\t1.2.11-40.el9\tx86_64\ngpg-pubkey\t8483c65d-5ccc5b19\t(none)\n");
		Assert.Equal([new InstalledPackage("gpg-pubkey", "8483c65d-5ccc5b19", ""), new InstalledPackage("zlib", "1.2.11-40.el9", "x86_64")], rpm);

		Assert.Equal([new InstalledPackage("linux", "6.10.10.arch1-1", "")], PackageListParser.ParsePacman("linux 6.10.10.arch1-1\n"));

		IReadOnlyList<InstalledPackage> apk = PackageListParser.ParseApkInstalled("musl-1.2.5-r0\npy3-foo-bar-2.0.1-r3\nbusybox-binsh-1.36.1-r29\nWARNING: noise\n");
		Assert.Equal(
			[new InstalledPackage("busybox-binsh", "1.36.1-r29", ""), new InstalledPackage("musl", "1.2.5-r0", ""), new InstalledPackage("py3-foo-bar", "2.0.1-r3", "")],
			apk);
	}

	[Fact]
	public void Packages_AptUpgradable()
	{
		IReadOnlyList<PackageUpdate> updates = PackageListParser.ParseAptUpgradable("""
			Listing...
			libssl3/bookworm-security 3.0.14-1~deb12u2 amd64 [upgradable from: 3.0.13-1~deb12u1]
			tzdata/stable-updates 2024b-0+deb12u1 all [upgradable from: 2024a-0+deb12u1]
			""");

		Assert.Equal(2, updates.Count);
		Assert.Equal("libssl3", updates[0].Name);
		Assert.Equal("bookworm-security", updates[0].Source);
		Assert.Equal("3.0.14-1~deb12u2", updates[0].NewVersion);
		Assert.Equal("3.0.13-1~deb12u1", updates[0].CurrentVersion);
		Assert.Equal("all", updates[1].Architecture);
	}

	[Fact]
	public void Packages_DnfCheckUpdate_StopsAtObsoletes()
	{
		IReadOnlyList<PackageUpdate> updates = PackageListParser.ParseCheckUpdate("""
			Last metadata expiration check: 0:12:44 ago on Sun 11 Oct 2026 08:00:00 AM UTC.

			kernel.x86_64                 5.14.0-427.33.1.el9_4      baseos
			openssl-libs.x86_64           1:3.0.7-28.el9_4           baseos
			Security: kernel-core-5.14.0-427.33.1.el9_4.x86_64 is an installed security update
			Obsoleting Packages
			grub2-tools.x86_64            1:2.06-82.el9              baseos
			""");

		Assert.Equal(["kernel", "openssl-libs"], updates.Select(update => update.Name));
		Assert.Equal("x86_64", updates[0].Architecture);
		Assert.Equal("1:3.0.7-28.el9_4", updates[1].NewVersion);
		Assert.Equal("baseos", updates[1].Source);
	}

	[Fact]
	public void Packages_ZypperListUpdates()
	{
		IReadOnlyList<PackageUpdate> updates = PackageListParser.ParseZypperUpdates("""
			S | Repository          | Name   | Current Version | Available Version | Arch
			--+---------------------+--------+-----------------+-------------------+-------
			v | repo-sle-update     | bash   | 4.4-150400.25.22| 4.4-150400.27.3.2 | x86_64
			v | repo-oss            | curl   | 8.0.1-1.1       | 8.6.0-1.1         | x86_64
			""");

		Assert.Equal(["bash", "curl"], updates.Select(update => update.Name));
		Assert.Equal("4.4-150400.27.3.2", updates[0].NewVersion);
		Assert.Equal("repo-sle-update", updates[0].Source);
	}

	[Fact]
	public void Packages_PacmanAndApkUpdates()
	{
		IReadOnlyList<PackageUpdate> pacman = PackageListParser.ParsePacmanUpdates("linux 6.10.9.arch1-1 -> 6.10.10.arch1-1\nvim 9.1.0-1 -> 9.1.1-1 [ignored]\n");
		Assert.Equal(["linux", "vim"], pacman.Select(update => update.Name));
		Assert.Equal("6.10.9.arch1-1", pacman[0].CurrentVersion);

		IReadOnlyList<PackageUpdate> apk = PackageListParser.ParseApkVersion("Installed:                                Available:\nbusybox-1.36.1-r28                      < 1.36.1-r29\n");
		PackageUpdate busybox = Assert.Single(apk);
		Assert.Equal("busybox", busybox.Name);
		Assert.Equal("1.36.1-r28", busybox.CurrentVersion);
		Assert.Equal("1.36.1-r29", busybox.NewVersion);
	}

	[Fact]
	public void Network_IpJson()
	{
		NetworkSnapshot snapshot = NetworkParser.Parse(NetworkFixtures.IpJson);

		Assert.False(snapshot.FromText);
		Assert.Equal(["lo", "eth0", "eth1"], snapshot.Interfaces.Select(item => item.Name));
		NetInterface eth0 = snapshot.Interfaces[1];
		Assert.True(eth0.IsUp);
		Assert.Equal("UP", eth0.State);
		Assert.Equal(1500, eth0.Mtu);
		Assert.Equal("52:54:00:12:34:56", eth0.Mac);
		Assert.Equal(["10.0.0.5/24", "fe80::5054:ff:fe12:3456/64"], eth0.Addresses.Select(address => address.Cidr));
		Assert.False(snapshot.Interfaces[2].IsUp);
		Assert.True(snapshot.Interfaces[0].IsLoopback);

		NetRoute route = snapshot.Routes[0];
		Assert.True(route.IsDefault);
		Assert.Equal("10.0.0.1", route.Gateway);
		Assert.Equal(100, route.Metric);
		Assert.True(snapshot.Routes[2].IsIpv6);

		(string scope, IReadOnlyList<string> servers) = Assert.Single(snapshot.Dns);
		Assert.Equal("eth0", scope);
		Assert.Equal(["10.0.0.1", "fe80::1%eth0"], servers);
		Assert.Equal(["lan"], snapshot.SearchDomains);

		Assert.Equal("ss", snapshot.ListeningSource);
		Assert.Equal(4, snapshot.Listening.Count);
		ListeningSocket ssh = snapshot.Listening.First(socket => socket.Port == 22 && socket.Address == "0.0.0.0");
		Assert.Equal("sshd", ssh.Process);
		Assert.Equal(801, ssh.Pid);
		Assert.Contains(snapshot.Listening, socket => socket.Address == "::" && socket.Port == 22);
		Assert.Contains(snapshot.Listening, socket => socket.Address == "127.0.0.53%lo" && socket.IsUdp);
		Assert.Equal("", snapshot.Listening.Single(socket => socket.Port == 80).Process);
	}

	[Fact]
	public void Network_BusyBoxText()
	{
		NetworkSnapshot snapshot = NetworkParser.Parse(NetworkFixtures.BusyBoxText);

		Assert.True(snapshot.FromText);
		NetInterface eth0 = snapshot.Interfaces.Single(item => item.Name == "eth0");
		Assert.True(eth0.IsUp);
		Assert.Equal(1500, eth0.Mtu);
		Assert.Equal("ether", eth0.LinkType);
		Assert.Equal("02:42:ac:11:00:02", eth0.Mac);
		Assert.Equal("172.17.0.2/16", Assert.Single(eth0.Addresses).Cidr);
		Assert.Equal("default", snapshot.Routes[0].Destination);
		Assert.Equal("172.17.0.1", snapshot.Routes[0].Gateway);
		Assert.Equal("172.17.0.2", snapshot.Routes[1].Source);
		Assert.Equal("resolv.conf", Assert.Single(snapshot.Dns).Scope);

		Assert.Equal("netstat", snapshot.ListeningSource);
		Assert.Equal(3, snapshot.Listening.Count);
		Assert.Contains(snapshot.Listening, socket => socket.Address == "::" && socket.Port == 22 && socket.Process == "sshd" && socket.Pid == 1);
		Assert.Contains(snapshot.Listening, socket => socket.IsUdp && socket.Port == 68 && socket.Process == "udhcpc");
	}

	[Theory]
	[InlineData("0.0.0.0:22", "0.0.0.0", 22)]
	[InlineData("[::]:443", "::", 443)]
	[InlineData("*:80", "*", 80)]
	[InlineData("[fe80::1%eth0]:546", "fe80::1%eth0", 546)]
	[InlineData(":::22", "::", 22)]
	[InlineData("nonsense", null, null)]
	public void Network_SplitsEndpoints(string endpoint, string? address, int? port) =>
		Assert.Equal((address, port), NetworkParser.SplitEndpoint(endpoint));

	[Fact]
	public void Disks_LsblkNewAndOld()
	{
		const string Current = """
			{
			   "blockdevices": [
			      {"name":"sda", "type":"disk", "size":500107862016, "fstype":null, "mountpoint":null, "label":null, "model":"Samsung SSD 870 ", "ro":false, "rm":false,
			         "children": [
			            {"name":"sda1", "type":"part", "size":536870912, "fstype":"vfat", "mountpoint":"/boot/efi", "label":null, "model":null, "ro":false, "rm":false},
			            {"name":"sda2", "type":"part", "size":499569721344, "fstype":"ext4", "mountpoint":"/", "label":"root", "model":null, "ro":false, "rm":false}
			         ]
			      },
			      {"name":"sr0", "type":"rom", "size":1073741312, "fstype":null, "mountpoint":null, "label":null, "model":"QEMU DVD-ROM", "ro":true, "rm":true}
			   ]
			}
			""";

		IReadOnlyList<BlockDevice> devices = DiskCommands.ParseLsblk(Current)!;
		Assert.Equal(["sda", "sr0"], devices.Select(device => device.Name));
		Assert.Equal("Samsung SSD 870", devices[0].Model);
		Assert.Equal(500107862016, devices[0].SizeBytes);
		Assert.Equal("/", devices[0].Children[1].MountPoint);
		Assert.True(devices[1].ReadOnly);
		Assert.True(devices[1].Removable);

		const string Old = """{"blockdevices": [{"name": "vda", "type": "disk", "size": "21474836480", "fstype": null, "mountpoint": null, "label": null, "model": null, "ro": "0", "rm": "1"}]}""";
		BlockDevice vda = Assert.Single(DiskCommands.ParseLsblk(Old)!);
		Assert.Equal(21474836480, vda.SizeBytes);
		Assert.False(vda.ReadOnly);
		Assert.True(vda.Removable);

		Assert.Null(DiskCommands.ParseLsblk("lsblk: unknown column"));
	}

	[Fact]
	public void Disks_DfMountsAndPartitions()
	{
		const string Output = """
			@@mt:partitions
			major minor  #blocks  name

			 254        0   20971520 vda
			 254        1   20970496 vda1
			@@mt:df
			Filesystem     1024-blocks    Used Available Capacity Mounted on
			/dev/vda1         20511312 6345432  13100920      33% /
			tmpfs               401020    1104    399916       1% /run
			/dev/loop3           65536   65536         0     100% /snap/core22/1612
			//nas/share     976762584 488381292 488381292     50% /mnt/my share
			@@mt:mounts
			/dev/vda1 / ext4 rw,relatime 0 0
			tmpfs /run tmpfs rw,nosuid,nodev 0 0
			/dev/loop3 /snap/core22/1612 squashfs ro,nodev 0 0
			//nas/share /mnt/my\040share cifs rw,relatime 0 0
			""";

		DiskSnapshot snapshot = DiskCommands.Parse(Output);

		Assert.True(snapshot.DevicesFromPartitions);
		Assert.Equal(["vda", "vda1"], snapshot.Devices.Select(device => device.Name));
		Assert.Equal(20971520L * 1024, snapshot.Devices[0].SizeBytes);
		Assert.Equal(4, snapshot.Usage.Count);
		Assert.Equal(["/", "/mnt/my share"], snapshot.Usage.Where(DiskCommands.IsStorage).Select(usage => usage.MountPoint));
		FileSystemUsage share = snapshot.Usage.Single(usage => usage.MountPoint == "/mnt/my share");
		Assert.Equal("cifs", share.Type);
		Assert.Equal(50, share.UsedPercent);
		Assert.Equal("/mnt/my share", snapshot.Mounts[3].MountPoint);
		Assert.True(snapshot.Mounts[2].IsReadOnly);
	}

	[Fact]
	public void Processes_Procps()
	{
		const string Output = """
			@@mt:memtotal
			MemTotal:        8037888 kB
			@@mt:procps
			      1 root                              0.0  0.1 13212 Ss   /sbin/init splash
			    801 root                              0.0  0.0  9012 Ss   sshd: /usr/sbin/sshd -D [listener] 0 of 10-100 startups
			   2211 www-data                         12.5  3.2 262144 Sl   nginx: worker process
			     12 root                              0.0  0.0     0 I    [kworker/0:1-events]
			""";

		IReadOnlyList<ProcessInfo> processes = ProcessCommands.Parse(Output);

		Assert.Equal(4, processes.Count);
		ProcessInfo nginx = processes.Single(process => process.Pid == 2211);
		Assert.Equal("www-data", nginx.User);
		Assert.Equal(12.5, nginx.Cpu);
		Assert.Equal(3.2, nginx.Memory);
		Assert.Equal(262144, nginx.ResidentKb);
		Assert.Equal("nginx: worker process", nginx.Command);
		Assert.Equal("nginx", nginx.Name);
		Assert.Equal("sshd", processes.Single(process => process.Pid == 801).Name);
		Assert.Equal("kworker/0:1-events", processes.Single(process => process.Pid == 12).Name);
		Assert.Equal("init", processes[0].Name);
	}

	[Fact]
	public void Processes_BusyBox_WorksOutMemoryShares()
	{
		const string Output = """
			@@mt:memtotal
			MemTotal:        1000000 kB
			@@mt:procps
			@@mt:busybox
			PID   USER     RSS  STAT COMMAND
			    1 root     1024 S    /sbin/init
			   42 nginx     12m S    nginx: worker process
			""";

		IReadOnlyList<ProcessInfo> processes = ProcessCommands.Parse(Output);

		Assert.Equal(2, processes.Count);
		Assert.Equal(12 * 1024, processes[1].ResidentKb);
		Assert.Equal(1.2, processes[1].Memory);
		Assert.Null(processes[1].Cpu);
		Assert.Equal("nginx: worker process", processes[1].Command);
	}

	[Fact]
	public void Accounts_PasswdGroupShadowAndLoginDefs()
	{
		const string Output = """
			@@mt:passwd
			root:x:0:0:root:/root:/bin/bash
			daemon:x:1:1:daemon:/usr/sbin:/usr/sbin/nologin
			alice:x:1000:1000:Alice Liddell,,,:/home/alice:/bin/bash
			bob:x:1001:1001::/home/bob:/bin/sh
			nobody:x:65534:65534:nobody:/nonexistent:/usr/sbin/nologin
			@@mt:group
			root:x:0:
			sudo:x:27:alice
			alice:x:1000:
			bob:x:1001:
			docker:x:999:alice,bob
			@@mt:shells
			# /etc/shells: valid login shells
			/bin/sh
			/bin/bash
			/usr/bin/bash
			@@mt:logindefs
			UID_MIN			 1000
			@@mt:shadow
			root:$
			daemon:*
			alice:$
			bob:!
			nobody:*
			""";

		AccountsSnapshot snapshot = AccountCommands.Parse(Output);

		Assert.True(snapshot.KnowsPasswords);
		Assert.Equal(1000, snapshot.FirstPersonUid);
		Assert.Equal(["/bin/sh", "/bin/bash", "/usr/bin/bash"], snapshot.Shells);
		UserAccount alice = snapshot.Users.Single(user => user.Name == "alice");
		Assert.Equal("Alice Liddell", alice.Comment);
		Assert.Equal(["alice", "sudo", "docker"], alice.Groups);
		Assert.Equal(PasswordState.Set, alice.Password);
		Assert.Equal(PasswordState.Locked, snapshot.Users.Single(user => user.Name == "bob").Password);
		Assert.Equal(PasswordState.Disabled, snapshot.Users.Single(user => user.Name == "daemon").Password);
		Assert.Equal(["root", "alice", "bob"], snapshot.Users.Where(snapshot.IsPerson).Select(user => user.Name));
		Assert.True(snapshot.Users.Single(user => user.Name == "daemon").HasNoLogin);
		Assert.Equal(["alice", "bob"], snapshot.Groups.Single(group => group.Name == "docker").Members);
	}

	[Fact]
	public void Accounts_WithoutShadow_KnowNoPasswords()
	{
		AccountsSnapshot snapshot = AccountCommands.Parse("@@mt:passwd\nalice:x:1000:1000::/home/alice:/bin/bash\n@@mt:group\n");

		Assert.False(snapshot.KnowsPasswords);
		Assert.Equal(PasswordState.Unknown, Assert.Single(snapshot.Users).Password);
	}

	[Fact]
	public void Clock_TimedatectlShow()
	{
		ClockInfo clock = ClockCommands.Parse("""
			@@mt:date
			2026-10-11 09:41:07 CEST (+0200)
			@@mt:epoch
			1791704467
			@@mt:show
			Timezone=Europe/Berlin
			LocalRTC=no
			CanNTP=yes
			NTP=yes
			NTPSynchronized=yes
			TimeUSec=Sun 2026-10-11 09:41:07 CEST
			@@mt:status
			               Local time: Sun 2026-10-11 09:41:07 CEST
			                Time zone: Europe/Berlin (CEST, +0200)
			System clock synchronized: yes
			              NTP service: active
			@@mt:tzfile
			Europe/Berlin
			@@mt:localtime
			/usr/share/zoneinfo/Europe/Berlin
			""");

		Assert.Equal("2026-10-11 09:41:07 CEST (+0200)", clock.LocalTime);
		Assert.Equal(1791704467, clock.Epoch);
		Assert.Equal("Europe/Berlin", clock.TimeZone);
		Assert.True(clock.NtpEnabled);
		Assert.True(clock.Synchronized);
		Assert.True(clock.CanSwitchNtp);
		Assert.False(clock.RtcInLocalTime);
		Assert.Equal("timedatectl", clock.SyncSource);
	}

	[Fact]
	public void Clock_OldTimedatectlStatus_AndClassicFiles()
	{
		ClockInfo old = ClockCommands.Parse("""
			@@mt:show
			@@mt:status
			      Local time: Sun 2026-10-11 07:41:07 UTC
			       Time zone: UTC (UTC, +0000)
			     NTP enabled: yes
			NTP synchronized: no
			 RTC in local TZ: no
			""");
		Assert.Equal("UTC", old.TimeZone);
		Assert.False(old.Synchronized);

		ClockInfo classic = ClockCommands.Parse("""
			@@mt:localtime
			../usr/share/zoneinfo/America/New_York
			@@mt:chrony
			Reference ID    : A9FEA97B (169.254.169.123)
			Leap status     : Normal
			""");
		Assert.Equal("America/New_York", classic.TimeZone);
		Assert.True(classic.Synchronized);
		Assert.Equal("chrony", classic.SyncSource);
		Assert.Null(classic.NtpEnabled);
	}

	[Fact]
	public void Clock_Zones_FromEitherListing()
	{
		IReadOnlyList<string> zones = ClockCommands.ParseZones("""
			Africa/Abidjan
			America/Argentina/Buenos_Aires
			UTC
			posix/Europe/Berlin
			right/UTC
			zone.tab
			posixrules
			SystemV/EST5
			Etc/GMT+5
			""");

		Assert.Equal(["Africa/Abidjan", "America/Argentina/Buenos_Aires", "Etc/GMT+5", "UTC"], zones);
	}

	[Fact]
	public void HostName_Hostnamectl()
	{
		HostNameInfo info = HostNameCommands.Parse("""
			@@mt:current
			web1
			@@mt:file
			web1
			@@mt:ctl
			 Static hostname: web1
			 Pretty hostname: Web server
			       Icon name: computer-vm
			         Chassis: vm 🖴
			      Machine ID: 0123456789abcdef0123456789abcdef
			  Virtualization: kvm
			Operating System: Debian GNU/Linux 12 (bookworm)
			""");

		Assert.Equal("web1", info.Current);
		Assert.Equal("web1", info.Static);
		Assert.Equal("Web server", info.Pretty);
		Assert.Contains(info.Details, detail => detail.Key == "Virtualization" && detail.Value == "kvm");
		Assert.DoesNotContain(info.Details, detail => detail.Key == "Static hostname");
	}

	[Fact]
	public void HostName_Classic()
	{
		HostNameInfo info = HostNameCommands.Parse("@@mt:current\nbox\n@@mt:file\n# set at install\nbox.example\n");

		Assert.Equal("box", info.Current);
		Assert.Equal("box.example", info.Static);
		Assert.Empty(info.Details);
	}

	[Fact]
	public void Overview_ReadsProcAndDf()
	{
		OverviewSnapshot snapshot = OverviewCommands.Parse("""
			@@mt:uptime
			1036810.27 4031722.10
			@@mt:loadavg
			0.08 0.12 0.10 1/312 4242
			@@mt:now
			1791704467
			@@mt:cpu
			model name	: AMD EPYC 7763 64-Core Processor
			@@mt:cores
			4
			@@mt:meminfo
			MemTotal:        8037888 kB
			MemFree:          512000 kB
			MemAvailable:    6000000 kB
			SwapTotal:       2097148 kB
			SwapFree:        2097148 kB
			@@mt:df
			Filesystem     1024-blocks    Used Available Capacity Mounted on
			/dev/vda1         20511312 6345432  13100920      33% /
			tmpfs               401020    1104    399916       1% /run
			@@mt:mounts
			/dev/vda1 / ext4 rw 0 0
			tmpfs /run tmpfs rw 0 0
			""");

		Assert.Equal(TimeSpan.FromSeconds(1036810), snapshot.Uptime);
		Assert.Equal(0.08, snapshot.Load1);
		Assert.Equal(0.10, snapshot.Load15);
		Assert.Equal("AMD EPYC 7763 64-Core Processor", snapshot.CpuModel);
		Assert.Equal(4, snapshot.Cores);
		Assert.Equal(6000000, snapshot.MemoryAvailableKb);
		Assert.Equal("/", Assert.Single(snapshot.Disks).MountPoint);
		Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1791704467 - 1036810), snapshot.BootedAt);
	}

	[Fact]
	public void Overview_OldKernel_WorksOutAvailableMemory()
	{
		OverviewSnapshot snapshot = OverviewCommands.Parse("@@mt:meminfo\nMemTotal: 1000 kB\nMemFree: 100 kB\nBuffers: 50 kB\nCached: 250 kB\n");

		Assert.Equal(400, snapshot.MemoryAvailableKb);
	}

	[Fact]
	public void Timers_SystemctlShowBlocks()
	{
		IReadOnlyList<SystemTimer> timers = CronCommands.ParseTimers("""
			Id=logrotate.timer
			Description=Daily rotation of log files
			ActiveState=active
			NextElapseUSecRealtime=Mon 2026-10-12 00:00:00 UTC
			LastTriggerUSec=Sun 2026-10-11 00:00:01 UTC
			Unit=logrotate.service
			TimersCalendar={ OnCalendar=*-*-* 00:00:00 ; next_elapse=Mon 2026-10-12 00:00:00 UTC }
			TimersMonotonic=

			Id=systemd-tmpfiles-clean.timer
			Description=Daily Cleanup of Temporary Directories
			ActiveState=active
			NextElapseUSecRealtime=
			LastTriggerUSec=n/a
			Unit=systemd-tmpfiles-clean.service
			TimersCalendar=
			TimersMonotonic={ OnBootUSec=15min ; next_elapse=15min } { OnUnitActiveUSec=1d ; next_elapse=1d 15min }
			""");

		Assert.Equal(["logrotate.timer", "systemd-tmpfiles-clean.timer"], timers.Select(timer => timer.Name));
		Assert.Equal("Mon 2026-10-12 00:00:00 UTC", timers[0].Next);
		Assert.Equal("logrotate.service", timers[0].Activates);
		Assert.Equal("OnCalendar=*-*-* 00:00:00", timers[0].Schedule);
		Assert.Equal("", timers[1].Last);
		Assert.Equal("OnBootSec=15min; OnUnitActiveSec=1d", timers[1].Schedule);
	}

	[Fact]
	public void Firewall_UfwActive()
	{
		FirewallStatus status = FirewallCommands.ParseUfw(FirewallFixtures.UfwActive);

		Assert.True(status.Active);
		Assert.Contains(status.Summary, item => item.Key == "Default" && item.Value.StartsWith("deny (incoming)", StringComparison.Ordinal));
		Assert.Equal(4, status.Rules.Count);
		Assert.Equal(new FirewallRule("22/tcp", "ALLOW IN", "Anywhere"), status.Rules[0]);
		Assert.Equal(new FirewallRule("3306", "DENY IN", "10.0.0.0/8"), status.Rules[2]);
		Assert.Equal(new FirewallRule("22/tcp (v6)", "ALLOW IN", "Anywhere (v6)"), status.Rules[3]);

		Assert.True(new FirewallCommands(FirewallKind.Ufw, 22).AllowsSsh(status));
		Assert.True(new FirewallCommands(FirewallKind.Ufw, 443).AllowsSsh(status));
		Assert.False(new FirewallCommands(FirewallKind.Ufw, 2222).AllowsSsh(status));
	}

	[Fact]
	public void Firewall_UfwInactive_KnowsNothingAboutSsh()
	{
		FirewallStatus status = FirewallCommands.ParseUfw(FirewallFixtures.UfwInactive);

		Assert.False(status.Active);
		Assert.Empty(status.Rules);
		Assert.Null(new FirewallCommands(FirewallKind.Ufw, 22).AllowsSsh(status));
	}

	[Fact]
	public void Firewall_Firewalld()
	{
		FirewallStatus status = FirewallCommands.ParseFirewalld(FirewallFixtures.FirewalldRunning);

		Assert.True(status.Active);
		Assert.Contains(("Default zone", "public"), status.Summary);
		Assert.Contains(("interfaces", "eth0"), status.Summary);
		Assert.Equal(
			["cockpit", "dhcpv6-client", "ssh", "8080/tcp", "5000-5010/udp", "rule family=\"ipv4\" source address=\"192.0.2.0/24\" port port=\"9000\" protocol=\"tcp\" reject"],
			status.Rules.Select(rule => rule.Target));
		Assert.True(new FirewallCommands(FirewallKind.Firewalld, 22).AllowsSsh(status));
		Assert.True(new FirewallCommands(FirewallKind.Firewalld, 8080).AllowsSsh(status));
		Assert.False(new FirewallCommands(FirewallKind.Firewalld, 2222).AllowsSsh(status));

		Assert.False(FirewallCommands.ParseFirewalld(FirewallFixtures.FirewalldStopped).Active);
	}

	[Fact]
	public void Sections_SplitOnTheirMarkers()
	{
		AdminSections sections = AdminSections.Parse("noise\n@@mt:one\na\nb\n@@mt:file /etc/x y\nz\n@@mt:two\n@@mt:file /etc/q\nq\n");

		Assert.Equal("a\nb\n", sections.Get("one"));
		Assert.Equal("", sections.Get("two"));
		Assert.Null(sections.Get("missing"));
		Assert.Equal(new[] { ("/etc/x y", "z\n"), ("/etc/q", "q\n") }, sections.All("file"));
		Assert.Equal("echo '@@mt:one'", AdminSections.Start("one"));
	}

	[Fact]
	public void Clean_DropsEscapesAndControlCharacters() =>
		Assert.Equal("red plain\ttab\nnext title gone", AdminText.Clean("\u001b[31mred\u001b[0m plain\ttab\r\nnext\u0007 title\u001b]0;window\u0007 gone"));
}
