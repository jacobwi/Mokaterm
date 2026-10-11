using Mokaterm.Modules.Ssh.Admin;
using Mokaterm.Modules.Ssh.Admin.Accounts;
using Mokaterm.Modules.Ssh.Admin.Clock;
using Mokaterm.Modules.Ssh.Admin.Detection;
using Mokaterm.Modules.Ssh.Admin.Firewall;
using Mokaterm.Modules.Ssh.Admin.SystemLogs;
using Mokaterm.Modules.Ssh.Admin.Naming;
using Mokaterm.Modules.Ssh.Admin.Network;
using Mokaterm.Modules.Ssh.Admin.Packages;
using Mokaterm.Modules.Ssh.Admin.Processes;
using Mokaterm.Modules.Ssh.Admin.Services;

namespace Mokaterm.Modules.Ssh.Tests.Admin;

/// <summary>The exact script text each family gets, since that text is what the user approves before it runs.</summary>
public sealed class CommandBuilderTests
{
	private const string AptGet = "apt-get -y -o Dpkg::Options::=--force-confdef -o Dpkg::Options::=--force-confold";

	[Theory]
	[InlineData(nameof(PackageManagerKind.Apt), "export DEBIAN_FRONTEND=noninteractive\n" + AptGet + " install 'htop'")]
	[InlineData(nameof(PackageManagerKind.Dnf), "dnf -y install 'htop'")]
	[InlineData(nameof(PackageManagerKind.Yum), "yum -y install 'htop'")]
	[InlineData(nameof(PackageManagerKind.Zypper), "zypper --non-interactive install 'htop'")]
	[InlineData(nameof(PackageManagerKind.Pacman), "pacman -S --noconfirm --needed 'htop'")]
	[InlineData(nameof(PackageManagerKind.Apk), "apk add 'htop'")]
	public void Install_PerManager(string manager, string expected)
	{
		AdminAction action = new PackageCommands(Enum.Parse<PackageManagerKind>(manager)).Install("htop");

		Assert.Equal(expected, action.Script.Body);
		Assert.True(action.Script.AsRoot);
		Assert.Equal(AdminScript.PackageTimeout, action.Script.Timeout);
		Assert.False(action.Destructive);
	}

	[Theory]
	[InlineData(nameof(PackageManagerKind.Apt), "export DEBIAN_FRONTEND=noninteractive\n" + AptGet + " remove 'nginx'")]
	[InlineData(nameof(PackageManagerKind.Dnf), "dnf -y remove 'nginx'")]
	[InlineData(nameof(PackageManagerKind.Yum), "yum -y remove 'nginx'")]
	[InlineData(nameof(PackageManagerKind.Zypper), "zypper --non-interactive remove 'nginx'")]
	[InlineData(nameof(PackageManagerKind.Pacman), "pacman -R --noconfirm 'nginx'")]
	[InlineData(nameof(PackageManagerKind.Apk), "apk del 'nginx'")]
	public void Remove_PerManager_IsDestructive(string manager, string expected)
	{
		AdminAction action = new PackageCommands(Enum.Parse<PackageManagerKind>(manager)).Remove("nginx");

		Assert.Equal(expected, action.Script.Body);
		Assert.True(action.Destructive);
		Assert.Null(action.Warning);
	}

	[Theory]
	[InlineData("openssh-server")]
	[InlineData("sudo")]
	[InlineData("systemd")]
	[InlineData("linux-image-6.1.0-25-amd64")]
	[InlineData("kernel-core")]
	[InlineData("libc6:amd64")]
	public void Remove_OfWhatTheMachineNeeds_Warns(string name) =>
		Assert.NotNull(new PackageCommands(PackageManagerKind.Apt).Remove(name).Warning);

	[Theory]
	[InlineData(nameof(PackageManagerKind.Apt), "set -e\nexport DEBIAN_FRONTEND=noninteractive\napt-get update\n" + AptGet + " upgrade")]
	[InlineData(nameof(PackageManagerKind.Dnf), "dnf -y upgrade")]
	[InlineData(nameof(PackageManagerKind.Yum), "yum -y update")]
	[InlineData(nameof(PackageManagerKind.Zypper), "zypper --non-interactive update")]
	[InlineData(nameof(PackageManagerKind.Pacman), "pacman -Syu --noconfirm")]
	[InlineData(nameof(PackageManagerKind.Apk), "set -e\napk update\napk upgrade")]
	public void UpgradeAll_PerManager(string manager, string expected) =>
		Assert.Equal(expected, new PackageCommands(Enum.Parse<PackageManagerKind>(manager)).UpgradeAll().Script.Body);

	[Theory]
	[InlineData(nameof(PackageManagerKind.Apt), "apt list --upgradable 2>/dev/null")]
	[InlineData(nameof(PackageManagerKind.Dnf), "dnf -q check-update\ncode=$?\nif [ \"$code\" = 100 ]; then exit 0; fi\nexit \"$code\"")]
	[InlineData(nameof(PackageManagerKind.Zypper), "zypper --non-interactive --quiet list-updates")]
	[InlineData(nameof(PackageManagerKind.Apk), "apk version -l '<' 2>/dev/null")]
	public void ListUpdates_PerManager_ReadsWithoutRoot(string manager, string expected)
	{
		AdminScript script = new PackageCommands(Enum.Parse<PackageManagerKind>(manager)).ListUpdates();

		Assert.Equal(expected, script.Body);
		Assert.False(script.AsRoot);
	}

	[Fact]
	public void Pacman_HasNoRefresh_BecauseSyAloneIsAPartialUpgrade()
	{
		Assert.Null(new PackageCommands(PackageManagerKind.Pacman).RefreshIndex());
		Assert.Equal("apt-get update", new PackageCommands(PackageManagerKind.Apt).RefreshIndex()!.Script.Body);
	}

	[Theory]
	[InlineData("-rf")]
	[InlineData("foo bar")]
	[InlineData("foo;rm")]
	[InlineData("$(id)")]
	[InlineData("")]
	public void Packages_RefuseNamesThatAreNotPackageNames(string name)
	{
		Assert.Throws<ArgumentException>(() => new PackageCommands(PackageManagerKind.Apt).Install(name));
		Assert.Throws<ArgumentException>(() => new PackageCommands(PackageManagerKind.Dnf).Remove(name));
	}

	[Theory]
	[InlineData(nameof(ServiceAction.Start), "systemctl start -- 'nginx.service'")]
	[InlineData(nameof(ServiceAction.Stop), "systemctl stop -- 'nginx.service'")]
	[InlineData(nameof(ServiceAction.Restart), "systemctl restart -- 'nginx.service'")]
	[InlineData(nameof(ServiceAction.Enable), "systemctl enable -- 'nginx.service'")]
	[InlineData(nameof(ServiceAction.Disable), "systemctl disable -- 'nginx.service'")]
	public void Systemd_Actions(string action, string expected)
	{
		AdminAction built = Services(InitSystem.Systemd).Act(Enum.Parse<ServiceAction>(action), "nginx.service");

		Assert.Equal(expected, built.Script.Body);
		Assert.True(built.Script.AsRoot);
	}

	[Theory]
	[InlineData(nameof(ServiceAction.Start), "rc-service 'sshd' start")]
	[InlineData(nameof(ServiceAction.Restart), "rc-service 'sshd' restart")]
	[InlineData(nameof(ServiceAction.Enable), "rc-update add 'sshd' default")]
	[InlineData(nameof(ServiceAction.Disable), "rc-update --all delete 'sshd'")]
	public void OpenRc_Actions(string action, string expected) =>
		Assert.Equal(expected, Services(InitSystem.OpenRc).Act(Enum.Parse<ServiceAction>(action), "sshd").Script.Body);

	[Fact]
	public void SysV_UsesServiceAndUpdateRcD()
	{
		ServiceCommands services = Services(InitSystem.SysV, "service", "update-rc.d");

		Assert.Equal("service 'cron' restart", services.Act(ServiceAction.Restart, "cron").Script.Body);
		Assert.Equal("set -e\nupdate-rc.d 'cron' defaults\nupdate-rc.d 'cron' enable", services.Act(ServiceAction.Enable, "cron").Script.Body);
		Assert.Equal("update-rc.d 'cron' disable", services.Act(ServiceAction.Disable, "cron").Script.Body);
	}

	[Fact]
	public void SysV_OnRedHat_UsesChkconfig()
	{
		ServiceCommands services = Services(InitSystem.SysV, "service", "chkconfig");

		Assert.Equal("chkconfig 'httpd' on", services.Act(ServiceAction.Enable, "httpd").Script.Body);
		Assert.Equal("chkconfig 'httpd' off", services.Act(ServiceAction.Disable, "httpd").Script.Body);
	}

	[Fact]
	public void BusyBoxInit_RunsTheScriptItself_AndCannotSwitchBoot()
	{
		ServiceCommands services = Services(InitSystem.BusyBox);

		Assert.Equal("'/etc/init.d/dropbear' restart", services.Act(ServiceAction.Restart, "dropbear").Script.Body);
		Assert.False(services.CanEnable);
	}

	[Theory]
	[InlineData(nameof(ServiceAction.Stop), "ssh.service", true)]
	[InlineData(nameof(ServiceAction.Restart), "sshd.service", true)]
	[InlineData(nameof(ServiceAction.Disable), "ssh", true)]
	[InlineData(nameof(ServiceAction.Stop), "NetworkManager.service", true)]
	[InlineData(nameof(ServiceAction.Start), "ssh.service", false)]
	[InlineData(nameof(ServiceAction.Stop), "nginx.service", false)]
	public void ServiceActions_ThatCanCutTheSession_Warn(string action, string name, bool warns) =>
		Assert.Equal(warns, Services(InitSystem.Systemd).Act(Enum.Parse<ServiceAction>(action), name).Warning is not null);

	[Fact]
	public void Systemd_Status_CarriesTheJournal_AndNeverFails()
	{
		AdminScript status = Services(InitSystem.Systemd).Status("cron.service", asRoot: true);

		Assert.Equal("systemctl status --no-pager --full --lines=40 -- 'cron.service' 2>&1\nexit 0", status.Body);
		Assert.True(status.AsRoot);
	}

	[Theory]
	[InlineData("nginx.service; reboot")]
	[InlineData("-x")]
	[InlineData("a b")]
	public void Services_RefuseBadNames(string name) =>
		Assert.Throws<ArgumentException>(() => Services(InitSystem.Systemd).Act(ServiceAction.Start, name));

	[Fact]
	public void Services_AcceptSystemdEscapes() =>
		Assert.Equal(
			@"systemctl restart -- 'systemd-fsck@dev-disk-by\x2duuid-1234.service'",
			Services(InitSystem.Systemd).Act(ServiceAction.Restart, @"systemd-fsck@dev-disk-by\x2duuid-1234.service").Script.Body);

	[Fact]
	public void Clock_Timedatectl()
	{
		ClockCommands clock = new(timedatectl: true);

		Assert.Equal("timedatectl set-timezone 'Europe/Berlin'", clock.SetTimeZone("Europe/Berlin").Script.Body);
		Assert.Equal("timedatectl set-ntp true", clock.SetNtp(true).Script.Body);
		Assert.True(clock.SetNtp(false).Destructive);
	}

	[Fact]
	public void Clock_Classic_LinksLocaltimeAndWritesTheZoneFile()
	{
		ClockCommands clock = new(timedatectl: false);

		Assert.Equal(
			"if [ ! -f '/usr/share/zoneinfo/America/Argentina/Buenos_Aires' ]; then echo 'This machine has no such time zone.' >&2; exit 1; fi\n"
				+ "ln -sf '/usr/share/zoneinfo/America/Argentina/Buenos_Aires' /etc/localtime\n"
				+ "if [ -f /etc/timezone ]; then printf '%s\\n' 'America/Argentina/Buenos_Aires' > /etc/timezone; fi",
			clock.SetTimeZone("America/Argentina/Buenos_Aires").Script.Body);
		Assert.Throws<InvalidOperationException>(() => clock.SetNtp(true));
	}

	[Theory]
	[InlineData("../../etc/passwd")]
	[InlineData("Europe/../../etc")]
	[InlineData("Europe/Berlin; id")]
	[InlineData("/etc/localtime")]
	public void Clock_RefusesZonesThatAreNotZones(string zone) =>
		Assert.Throws<ArgumentException>(() => new ClockCommands(timedatectl: false).SetTimeZone(zone));

	[Fact]
	public void HostName_HostnamectlOrTheFile()
	{
		Assert.Equal("hostnamectl set-hostname 'web-02'", new HostNameCommands(hostnamectl: true).Set("web-02", null).Script.Body);
		Assert.Equal(
			"set -e\nprintf '%s\\n' 'web-02.example.com' > /etc/hostname\nhostname 'web-02.example.com'",
			new HostNameCommands(hostnamectl: false).Set("web-02.example.com", null).Script.Body);
	}

	[Fact]
	public void HostName_WarnsWhenTheSessionUsesTheNameButNotForAnAddress()
	{
		HostNameCommands commands = new(hostnamectl: true);

		Assert.NotNull(commands.Set("web-02", "web-01.lan").Warning);
		Assert.Null(commands.Set("web-02", "10.0.0.5").Warning);
		Assert.Null(commands.Set("web-02", "WEB-02").Warning);
	}

	[Theory]
	[InlineData("-bad")]
	[InlineData("bad-")]
	[InlineData("under_score")]
	[InlineData("a..b")]
	[InlineData("")]
	public void HostName_RefusesInvalidNames(string name) =>
		Assert.Throws<ArgumentException>(() => new HostNameCommands(hostnamectl: true).Set(name, null));

	[Fact]
	public void Accounts_Shadow()
	{
		AccountCommands accounts = new(AccountToolKind.Shadow, hasChpasswd: true);

		(AdminAction add, string? input) = accounts.AddUser("alice", "Alice Liddell", "/bin/bash", createHome: true, password: "s3cret pass");
		Assert.Equal("set -e\nuseradd -m -s '/bin/bash' -c 'Alice Liddell' -- 'alice'\nchpasswd", add.Script.Body);
		Assert.True(add.Script.ReadsInput);
		Assert.Equal("alice:s3cret pass", input);
		Assert.DoesNotContain("s3cret", add.Script.Body, StringComparison.Ordinal);

		(AdminAction bare, string? none) = accounts.AddUser("bob", null, "/bin/sh", createHome: false, password: null);
		Assert.Equal("useradd -M -s '/bin/sh' -- 'bob'", bare.Script.Body);
		Assert.False(bare.Script.ReadsInput);
		Assert.Null(none);

		Assert.Equal("usermod -L -- 'alice'", accounts.Lock("alice", locked: true).Script.Body);
		Assert.Equal("usermod -U -- 'alice'", accounts.Lock("alice", locked: false).Script.Body);
		Assert.Equal("usermod -a -G 'docker' -- 'alice'", accounts.AddToGroup("alice", "docker").Script.Body);
		Assert.NotNull(accounts.AddToGroup("alice", "sudo").Warning);

		UserAccount user = new() { Name = "carol", Uid = 1002, Home = "/home/carol" };
		Assert.Equal("userdel -r -- 'carol'", accounts.DeleteUser(user, removeHome: true, "alice").Script.Body);
		Assert.Equal("userdel -- 'carol'", accounts.DeleteUser(user, removeHome: false, "alice").Script.Body);

		(AdminAction password, string secret) = accounts.SetPassword("carol", "n3w");
		Assert.Equal("chpasswd", password.Script.Body);
		Assert.Equal("carol:n3w", secret);
	}

	[Fact]
	public void Accounts_BusyBox()
	{
		AccountCommands accounts = new(AccountToolKind.BusyBox, hasChpasswd: true);

		Assert.Equal("adduser -D -s '/bin/ash' -g 'Al' 'al'", accounts.AddUser("al", "Al", "/bin/ash", createHome: true, password: null).Action.Script.Body);
		Assert.Equal("adduser -D -H -s '/bin/ash' 'al'", accounts.AddUser("al", null, "/bin/ash", createHome: false, password: null).Action.Script.Body);
		Assert.Equal("passwd -l 'al'", accounts.Lock("al", locked: true).Script.Body);
		Assert.Equal("addgroup 'al' 'wheel'", accounts.AddToGroup("al", "wheel").Script.Body);
		Assert.Equal("deluser --remove-home 'al'", accounts.DeleteUser(new UserAccount { Name = "al", Uid = 1000 }, removeHome: true, "root").Script.Body);
	}

	[Fact]
	public void Accounts_RefuseRootAndTheSessionsOwnLogin()
	{
		AccountCommands accounts = new(AccountToolKind.Shadow, hasChpasswd: true);

		Assert.Throws<InvalidOperationException>(() => accounts.DeleteUser(new UserAccount { Name = "root", Uid = 0 }, false, "alice"));
		Assert.Throws<InvalidOperationException>(() => accounts.DeleteUser(new UserAccount { Name = "alice", Uid = 1000 }, false, "alice"));
		Assert.NotNull(AccountCommands.WhyNotDelete(new UserAccount { Name = "toor", Uid = 0 }, "alice"));
	}

	[Theory]
	[InlineData("bad name", null, "/bin/sh", null)]
	[InlineData("-o", null, "/bin/sh", null)]
	[InlineData("ok", "Full: Name", "/bin/sh", null)]
	[InlineData("ok", null, "bash", null)]
	[InlineData("ok", null, "/bin/sh", "two\nlines")]
	public void Accounts_ValidateEveryValue(string name, string? comment, string shell, string? password) =>
		Assert.Throws<ArgumentException>(() => new AccountCommands(AccountToolKind.Shadow, true).AddUser(name, comment, shell, true, password));

	[Fact]
	public void Accounts_WithoutTools_ChangeNothing()
	{
		AccountCommands accounts = new(AccountToolKind.None, hasChpasswd: false);

		Assert.False(accounts.CanChange);
		Assert.Throws<InvalidOperationException>(() => accounts.Lock("alice", true));
	}

	[Fact]
	public void Firewall_Ufw()
	{
		FirewallCommands ufw = new(FirewallKind.Ufw, sshPort: 22);

		Assert.Equal("ufw status verbose 2>&1", ufw.Status().Body);
		Assert.True(ufw.Status().AsRoot);
		Assert.Equal("ufw allow '8080/tcp'", ufw.Allow(new FirewallPort(8080, "tcp")).Script.Body);
		Assert.Equal("ufw deny '53'", ufw.Deny(new FirewallPort(53, "")).Script.Body);
		Assert.Equal("ufw disable", ufw.Disable().Script.Body);
	}

	[Fact]
	public void Firewall_Ufw_Enable_AllowsTheSessionsPortFirst_UnlessARuleAlreadyDoes()
	{
		FirewallCommands ufw = new(FirewallKind.Ufw, sshPort: 2222);

		Assert.Equal("set -e\nufw allow 2222/tcp\nufw --force enable", ufw.Enable(null).Script.Body);

		FirewallStatus allowed = FirewallCommands.ParseUfw(FirewallFixtures.UfwActive.Replace("22/tcp ", "2222/tcp", StringComparison.Ordinal));
		Assert.Equal("ufw --force enable", ufw.Enable(allowed).Script.Body);
	}

	[Fact]
	public void Firewall_Deny_OfTheSessionsPort_Warns()
	{
		FirewallCommands ufw = new(FirewallKind.Ufw, sshPort: 22);

		AdminAction denySsh = ufw.Deny(new FirewallPort(22, "tcp"));
		Assert.NotNull(denySsh.Warning);
		Assert.Equal("Deny anyway", denySsh.ConfirmText);
		Assert.NotNull(ufw.Deny(new FirewallPort(22, "")).Warning);
		Assert.Null(ufw.Deny(new FirewallPort(22, "udp")).Warning);
		Assert.Null(ufw.Deny(new FirewallPort(8080, "tcp")).Warning);
	}

	[Fact]
	public void Firewall_Firewalld()
	{
		FirewallCommands firewalld = new(FirewallKind.Firewalld, sshPort: 22);

		Assert.Equal(
			"set -e\nfirewall-cmd --permanent '--add-port=8080/tcp'\nfirewall-cmd --reload",
			firewalld.Allow(new FirewallPort(8080, "tcp")).Script.Body);
		Assert.Equal(
			"set -e\nfirewall-cmd --permanent '--add-port=53/tcp'\nfirewall-cmd --permanent '--add-port=53/udp'\nfirewall-cmd --reload",
			firewalld.Allow(new FirewallPort(53, "")).Script.Body);
		Assert.Equal(
			"set -e\nfirewall-cmd --permanent '--remove-port=9000/tcp' || true\nfirewall-cmd --permanent '--add-rich-rule=rule port port=\"9000\" protocol=\"tcp\" reject'\nfirewall-cmd --reload",
			firewalld.Deny(new FirewallPort(9000, "tcp")).Script.Body);
		Assert.Equal(
			"set -e\nsystemctl enable --now firewalld\nfirewall-cmd --permanent --add-port=22/tcp\nfirewall-cmd --reload",
			firewalld.Enable(null).Script.Body);
		Assert.Equal("systemctl disable --now firewalld", firewalld.Disable().Script.Body);
	}

	[Fact]
	public void Firewall_RawRuleSets_AreReadOnly()
	{
		FirewallCommands nft = new(FirewallKind.Nftables, sshPort: 22);

		Assert.False(nft.CanChange);
		Assert.Equal("nft list ruleset 2>&1", nft.Status().Body);
		Assert.Throws<InvalidOperationException>(() => nft.Allow(new FirewallPort(80, "tcp")));
		Assert.Equal("iptables -S 2>&1\necho\nip6tables -S 2>&1", new FirewallCommands(FirewallKind.Iptables, 22).Status().Body);
	}

	[Theory]
	[InlineData("8080", 8080, "")]
	[InlineData("8080/tcp", 8080, "tcp")]
	[InlineData(" 53/UDP ", 53, "udp")]
	public void FirewallPort_Parses(string text, int port, string protocol)
	{
		FirewallPort? parsed = FirewallPort.TryParse(text, out string? problem);

		Assert.Null(problem);
		Assert.Equal(new FirewallPort(port, protocol), parsed);
	}

	[Theory]
	[InlineData("0")]
	[InlineData("65536")]
	[InlineData("80/sctp")]
	[InlineData("80/tcp/x")]
	[InlineData("http")]
	[InlineData("80;id")]
	[InlineData("")]
	public void FirewallPort_RefusesTheRest(string text)
	{
		Assert.Null(FirewallPort.TryParse(text, out string? problem));
		Assert.NotNull(problem);
	}

	[Fact]
	public void Network_SetLink_WarnsForTheSessionsInterfaceAndTheDefaultRoute()
	{
		NetworkSnapshot snapshot = NetworkParser.Parse(NetworkFixtures.IpJson);
		NetInterface eth0 = snapshot.Interfaces.Single(item => item.Name == "eth0");
		NetInterface eth1 = snapshot.Interfaces.Single(item => item.Name == "eth1");

		AdminAction down = NetworkCommands.SetLink(eth0, up: false, snapshot, "10.0.0.5");
		Assert.Equal("ip link set dev 'eth0' down", down.Script.Body);
		Assert.Contains("cuts this session", down.Warning, StringComparison.Ordinal);
		Assert.Equal("Bring down anyway", down.ConfirmText);
		Assert.True(down.Destructive);

		// The session is on eth1 here, but eth0 still carries the default route.
		Assert.Contains("default route", NetworkCommands.SetLink(eth0, up: false, snapshot, "192.168.50.2").Warning, StringComparison.Ordinal);
		Assert.Null(NetworkCommands.SetLink(eth1, up: false, snapshot, "10.0.0.5").Warning);
		Assert.Null(NetworkCommands.SetLink(eth0, up: true, snapshot, "10.0.0.5").Warning);
		Assert.Equal("ip link set dev 'eth1' up", NetworkCommands.SetLink(eth1, up: true, snapshot, null).Script.Body);
	}

	[Fact]
	public void Network_SetLink_MatchesAnIpv4MappedSessionAddress()
	{
		NetworkSnapshot snapshot = NetworkParser.Parse(NetworkFixtures.IpJson);
		NetInterface eth0 = snapshot.Interfaces.Single(item => item.Name == "eth0");

		Assert.Contains("cuts this session", NetworkCommands.SetLink(eth0, up: false, snapshot, "::ffff:10.0.0.5").Warning, StringComparison.Ordinal);
	}

	[Fact]
	public void Processes_Signal()
	{
		ProcessInfo nginx = new() { Pid = 1234, User = "www-data", Command = "nginx: worker process" };

		Assert.Equal("kill -s TERM 1234", ProcessCommands.Signal(nginx, force: false, asRoot: true).Script.Body);
		Assert.Equal("kill -s KILL 1234", ProcessCommands.Signal(nginx, force: true, asRoot: false).Script.Body);
		Assert.False(ProcessCommands.Signal(nginx, force: true, asRoot: false).Script.AsRoot);
		Assert.NotNull(ProcessCommands.Signal(new ProcessInfo { Pid = 900, Command = "sshd: alice@pts/0" }, false, true).Warning);
		Assert.Throws<ArgumentException>(() => ProcessCommands.Signal(new ProcessInfo { Pid = 1, Command = "/sbin/init" }, false, true));
	}

	[Fact]
	public void Logs_Journal_ClampsAndQuotes()
	{
		Assert.Equal("journalctl --no-pager -o short-iso -n 200 2>&1", LogCommands.Journal(null, null, 200, asRoot: false).Body);
		Assert.Equal(
			"journalctl --no-pager -o short-iso -n 2000 -u 'nginx.service' -p err 2>&1",
			LogCommands.Journal("nginx.service", "err", 999_999, asRoot: true).Body);
		Assert.Throws<ArgumentException>(() => LogCommands.Journal("x;id", null, 10, false));
		Assert.Throws<ArgumentException>(() => LogCommands.Journal(null, "loud", 10, false));
	}

	[Fact]
	public void Logs_Files_StayUnderVarLog()
	{
		Assert.Equal("tail -n 50 -- '/var/log/nginx/error.log'", LogCommands.ReadFile("/var/log/nginx/error.log", 50, false).Body);
		Assert.Throws<ArgumentException>(() => LogCommands.ReadFile("/var/log/../../etc/shadow", 50, true));
		Assert.Throws<ArgumentException>(() => LogCommands.ReadFile("/etc/shadow", 50, true));
		Assert.Equal(
			["/var/log/syslog", "/var/log/nginx/access.log"],
			LogCommands.ParseFiles("/var/log/syslog\n/var/log/nginx/access.log\n/var/log/weird name\n/tmp/x\n"));
	}

	[Fact]
	public void Capabilities_GiveEachFamilyItsCommands()
	{
		SystemCapabilities alpine = SystemProfile.From(SystemProbe.Parse(DetectionFixtures.Alpine)).Capabilities;
		SystemCapabilities rocky = SystemProfile.From(SystemProbe.Parse(DetectionFixtures.Rocky9)).Capabilities;

		Assert.Equal("apk add 'curl'", alpine.Packages!.Install("curl").Script.Body);
		Assert.Equal("rc-service 'crond' restart", alpine.Services!.Act(ServiceAction.Restart, "crond").Script.Body);
		Assert.Equal("adduser -D -s '/bin/sh' 'al'", alpine.Accounts.AddUser("al", null, "/bin/sh", true, null).Action.Script.Body);
		Assert.Equal("dnf -y install 'curl'", rocky.Packages!.Install("curl").Script.Body);
		Assert.Equal(FirewallKind.Firewalld, rocky.Firewall!.Kind);
	}

	private static ServiceCommands Services(InitSystem init, params string[] commands) =>
		new(init, new SystemFacts
		{
			Commands = new HashSet<string>(commands, StringComparer.Ordinal),
			Paths = new HashSet<string>(["/etc/init.d"], StringComparer.Ordinal),
		});
}
