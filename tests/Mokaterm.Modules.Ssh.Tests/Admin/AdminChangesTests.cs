using System.Text;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Modules.Ssh.Admin;
using Mokaterm.Modules.Ssh.Admin.Accounts;
using Mokaterm.Modules.Ssh.Admin.Detection;
using Mokaterm.Modules.Ssh.Admin.Firewall;
using Mokaterm.Modules.Ssh.Admin.Packages;
using Mokaterm.Modules.Ssh.Admin.Services;

namespace Mokaterm.Modules.Ssh.Tests.Admin;

/// <summary>
/// The system view's newer changes, as the text the user approves: groups, unit overrides, firewall rule deletes, and
/// the security update and reboot reads the packages page shows.
/// </summary>
public sealed class AdminChangesTests
{
	private const string UfwNumbered = """
		Status: active

		     To                         Action      From
		     --                         ------      ----
		[ 1] 22/tcp                     ALLOW IN    Anywhere
		[ 2] 80,443/tcp                 ALLOW IN    Anywhere
		[ 3] 3306                       DENY IN     10.0.0.0/8
		[ 4] 22/tcp (v6)                ALLOW IN    Anywhere (v6)

		""";

	[Theory]
	[InlineData(nameof(AccountToolKind.Shadow), "groupadd -- 'deploy'", "groupdel -- 'deploy'")]
	[InlineData(nameof(AccountToolKind.BusyBox), "addgroup 'deploy'", "delgroup 'deploy'")]
	public void Groups_AddAndDelete_PerTool(string tools, string add, string delete)
	{
		AccountCommands accounts = new(Enum.Parse<AccountToolKind>(tools), hasChpasswd: true);

		AdminAction added = accounts.AddGroup("deploy");
		AdminAction deleted = accounts.DeleteGroup(new GroupAccount("deploy", 1001, []), Snapshot(), "alice");

		Assert.Equal(add, added.Script.Body);
		Assert.True(added.Script.AsRoot);
		Assert.False(added.Destructive);
		Assert.Equal(delete, deleted.Script.Body);
		Assert.True(deleted.Destructive);
		Assert.False(deleted.CutsConnection);
		Assert.Throws<ArgumentException>(() => accounts.AddGroup("-x"));
	}

	[Fact]
	public void Groups_TheRootGroup_AndSomeonesPrimaryGroup_AreNotDeleted()
	{
		AccountsSnapshot snapshot = Snapshot();
		AccountCommands accounts = new(AccountToolKind.Shadow, hasChpasswd: true);

		Assert.NotNull(AccountCommands.WhyNotDeleteGroup(new GroupAccount("root", 0, []), snapshot));
		Assert.Contains("primary group of alice", AccountCommands.WhyNotDeleteGroup(new GroupAccount("alice", 1000, []), snapshot), StringComparison.Ordinal);
		Assert.Throws<InvalidOperationException>(() => accounts.DeleteGroup(new GroupAccount("alice", 1000, []), snapshot, "alice"));
		Assert.Null(AccountCommands.WhyNotDeleteGroup(new GroupAccount("deploy", 1001, []), snapshot));
	}

	[Fact]
	public void Groups_DeletingTheSudoGroupOfThisLogin_CutsTheConnection_AndASystemGroupWarns()
	{
		AccountCommands accounts = new(AccountToolKind.Shadow, hasChpasswd: true);

		AdminAction sudo = accounts.DeleteGroup(new GroupAccount("sudo", 27, ["alice"]), Snapshot(), "alice");
		AdminAction others = accounts.DeleteGroup(new GroupAccount("sudo", 27, ["bob"]), Snapshot(), "alice");

		Assert.True(sudo.CutsConnection);
		Assert.Contains("sudo", sudo.Warning, StringComparison.Ordinal);
		Assert.False(others.CutsConnection);
		Assert.Contains("system group", others.Warning, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("nginx.service", "/etc/systemd/system/nginx.service.d/override.conf")]
	[InlineData("nginx", "/etc/systemd/system/nginx.service.d/override.conf")]
	[InlineData("postgresql@16-main.service", "/etc/systemd/system/postgresql@16-main.service.d/override.conf")]
	[InlineData("backup.timer", "/etc/systemd/system/backup.timer.d/override.conf")]
	public void Overrides_LiveWhereSystemctlEditKeepsThem(string unit, string expected)
	{
		Assert.Equal(expected, ServiceCommands.OverridePath(unit));
		Assert.Equal(expected[..^"/override.conf".Length], ServiceCommands.OverrideFolder(unit));
	}

	[Theory]
	[InlineData("../etc")]
	[InlineData("a/b.service")]
	[InlineData("-x.service")]
	[InlineData("")]
	public void Overrides_RefuseNamesThatAreNotUnits(string unit) =>
		Assert.Throws<ArgumentException>(() => ServiceCommands.OverridePath(unit));

	[Fact]
	public void UnitFiles_AreSystemds_AndDaemonReloadRunsAsRoot()
	{
		ServiceCommands systemd = new(InitSystem.Systemd, new SystemFacts());
		ServiceCommands openrc = new(InitSystem.OpenRc, new SystemFacts());

		Assert.Equal("systemctl cat --no-pager -- 'nginx.service' 2>&1\nexit 0", systemd.Cat("nginx.service").Body);
		Assert.False(systemd.Cat("nginx.service").AsRoot);
		AdminAction reload = systemd.DaemonReload("nginx.service");
		Assert.Equal("systemctl daemon-reload", reload.Script.Body);
		Assert.True(reload.Script.AsRoot);
		Assert.False(reload.Destructive);
		Assert.False(openrc.HasUnitFiles);
		Assert.Throws<InvalidOperationException>(() => openrc.Cat("sshd"));
	}

	[Fact]
	public void Ufw_NumberedList_PutsNumbersOnTheRules()
	{
		FirewallStatus status = FirewallCommands.ParseUfwSections("@@mt:verbose\n" + FirewallFixtures.UfwActive.ReplaceLineEndings("\n") + "@@mt:numbered\n" + UfwNumbered.ReplaceLineEndings("\n"));

		Assert.Equal([1, 2, 3, 4], status.Rules.Select(rule => rule.Number));
		Assert.Equal("22/tcp                     ALLOW IN    Anywhere", status.Rules[0].NumberedText);
		Assert.Equal(new FirewallRule("3306", "DENY IN", "10.0.0.0/8"), status.Rules[2] with { Number = null, NumberedText = "" });
		Assert.True(status.Active);
	}

	[Fact]
	public void Ufw_ANumberedListThatDoesNotMatch_GivesNoNumbers()
	{
		string shifted = UfwNumbered.ReplaceLineEndings("\n").Replace("[ 1] 22/tcp                     ALLOW IN    Anywhere\n", "", StringComparison.Ordinal);

		FirewallStatus status = FirewallCommands.ParseUfwSections("@@mt:verbose\n" + FirewallFixtures.UfwActive.ReplaceLineEndings("\n") + "@@mt:numbered\n" + shifted);

		Assert.All(status.Rules, rule => Assert.Null(rule.Number));
		Assert.NotNull(new FirewallCommands(FirewallKind.Ufw, 22).WhyNotDelete(status.Rules[0]));
	}

	[Fact]
	public void Ufw_Delete_ChecksTheNumberStillMeansTheRule()
	{
		FirewallCommands ufw = new(FirewallKind.Ufw, 22);
		FirewallStatus status = UfwStatus();

		AdminAction action = ufw.Delete(status.Rules[2], status);

		Assert.Equal(string.Join('\n',
			"set -e",
			"current=$(ufw status numbered | sed -n 's/^\\[ *3\\] *//p' | sed 's/[[:space:]]*$//')",
			"if [ \"$current\" != '3306                       DENY IN     10.0.0.0/8' ]; then",
			"\techo 'The rules changed since they were read. Read them again and pick the rule anew.' >&2",
			"\texit 1",
			"fi",
			"ufw --force delete 3"), action.Script.Body);
		Assert.True(action.Script.AsRoot);
		Assert.True(action.Destructive);
		Assert.False(action.CutsConnection);
	}

	[Fact]
	public void Ufw_DeletingTheOnlyRuleForTheSessionsPort_InItsFamily_CutsTheConnection()
	{
		FirewallStatus status = UfwStatus();

		Assert.True(new FirewallCommands(FirewallKind.Ufw, 22).Delete(status.Rules[0], status).CutsConnection);
		Assert.True(new FirewallCommands(FirewallKind.Ufw, 443).Delete(status.Rules[1], status).CutsConnection);
		Assert.False(new FirewallCommands(FirewallKind.Ufw, 2222).Delete(status.Rules[0], status).CutsConnection);

		FirewallStatus off = status with { Active = false };
		Assert.False(new FirewallCommands(FirewallKind.Ufw, 22).Delete(off.Rules[0], off).CutsConnection);
	}

	[Fact]
	public void Firewalld_Delete_RemovesServicesPortsAndRichRules_Permanently()
	{
		FirewallCommands firewalld = new(FirewallKind.Firewalld, 22);
		FirewallStatus status = FirewallCommands.ParseFirewalld(FirewallFixtures.FirewalldRunning.ReplaceLineEndings("\n"));
		FirewallRule ssh = status.Rules.Single(rule => rule.Target == "ssh");
		FirewallRule port = status.Rules.Single(rule => rule.Target == "5000-5010/udp");
		FirewallRule rich = status.Rules.Single(rule => rule.IsRichRule);

		Assert.Equal("set -e\nfirewall-cmd --permanent '--remove-port=5000-5010/udp'\nfirewall-cmd --reload", firewalld.Delete(port, status).Script.Body);
		Assert.Equal(
			"set -e\nfirewall-cmd --permanent '--remove-rich-rule=rule family=\"ipv4\" source address=\"192.0.2.0/24\" port port=\"9000\" protocol=\"tcp\" reject'\nfirewall-cmd --reload",
			firewalld.Delete(rich, status).Script.Body);
		AdminAction removeSsh = firewalld.Delete(ssh, status);
		Assert.Equal("set -e\nfirewall-cmd --permanent '--remove-service=ssh'\nfirewall-cmd --reload", removeSsh.Script.Body);
		Assert.True(removeSsh.CutsConnection);
	}

	[Fact]
	public void Delete_RefusesWhatCannotBeDeletedSafely()
	{
		FirewallCommands firewalld = new(FirewallKind.Firewalld, 22);

		Assert.NotNull(firewalld.WhyNotDelete(new FirewallRule("ssh; reboot", "ALLOW", "service")));
		Assert.NotNull(firewalld.WhyNotDelete(new FirewallRule("99999999/tcp", "ALLOW", "port")));
		Assert.NotNull(firewalld.WhyNotDelete(new FirewallRule("public (active)", "ALLOW", "zone")));
		Assert.NotNull(new FirewallCommands(FirewallKind.Nftables, 22).WhyNotDelete(new FirewallRule("x", "ALLOW", "")));
		Assert.Throws<ArgumentException>(() => firewalld.Delete(new FirewallRule("ssh; reboot", "ALLOW", "service"), new FirewallStatus()));
	}

	[Fact]
	public async Task Ufw_TheDeletesSed_ReadsTheRuleTheParserRead()
	{
		if (PosixShellLocator.Find() is not { } shell)
		{
			Assert.Skip("No POSIX sh is available on this machine.");
			return;
		}

		FirewallStatus status = UfwStatus();
		string line = new FirewallCommands(FirewallKind.Ufw, 22).Delete(status.Rules[3], status).Script.Body.Split('\n')[1];
		string script = string.Join('\n',
			PosixShellLocator.UnixPathPrefix,
			"ufw() {",
			$"\tprintf '%s' {PosixShell.Quote(UfwNumbered.ReplaceLineEndings("\n"))}",
			"}",
			line,
			"printf '%s' \"$current\"");
		(int exit, byte[] output, string error) = await PosixShellLocator.RunAsync(shell, script, []).WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

		Assert.True(exit == 0, error);
		Assert.Equal(status.Rules[3].NumberedText, Encoding.UTF8.GetString(output));
	}

	[Theory]
	[InlineData("noble-security", true)]
	[InlineData("noble-updates,noble-security", true)]
	[InlineData("stable-security", true)]
	[InlineData("buster/updates", true)]
	[InlineData("noble-updates", false)]
	[InlineData("bookworm", false)]
	public void Apt_SecurityUpdates_AreTheOnesFromASecuritySuite(string source, bool security) =>
		Assert.Equal(security, PackageCommands.IsSecuritySource(source));

	[Fact]
	public void SecurityUpdates_AreReadFromTheManagersThatKeepThem()
	{
		PackageCommands apt = new(PackageManagerKind.Apt);
		PackageCommands dnf = new(PackageManagerKind.Dnf);

		Assert.Equal("apt list --upgradable 2>/dev/null", apt.ListSecurityUpdates()!.Body);
		Assert.Equal("packages.security", apt.ListSecurityUpdates()!.Name);
		Assert.Equal("dnf -q --security check-update\ncode=$?\nif [ \"$code\" = 100 ]; then exit 0; fi\nexit \"$code\"", dnf.ListSecurityUpdates()!.Body);
		Assert.StartsWith("yum -q --security check-update", new PackageCommands(PackageManagerKind.Yum).ListSecurityUpdates()!.Body, StringComparison.Ordinal);
		Assert.Null(new PackageCommands(PackageManagerKind.Pacman).ListSecurityUpdates());
		Assert.Null(new PackageCommands(PackageManagerKind.Apk).ListSecurityUpdates());
		Assert.Null(new PackageCommands(PackageManagerKind.Zypper).ListSecurityUpdates());
		Assert.False(new PackageCommands(PackageManagerKind.Zypper).KnowsSecurityUpdates);
	}

	[Fact]
	public void SecurityUpdates_ForApt_ComeOutOfTheOrdinaryList()
	{
		PackageCommands apt = new(PackageManagerKind.Apt);
		const string Listing = "Listing...\ncurl/noble-updates 8.5.0-2ubuntu10.5 amd64 [upgradable from: 8.5.0-2ubuntu10.4]\nlibssl3t64/noble-updates,noble-security 3.0.13-0ubuntu3.4 amd64 [upgradable from: 3.0.13-0ubuntu3.3]\n";

		Assert.Equal(["libssl3t64"], apt.ParseSecurityUpdates(Listing).Select(update => update.Name));
		Assert.Equal(["libssl3t64"], apt.SecurityUpdatesAmong(apt.ParseUpdates(Listing))!.Select(update => update.Name));
		Assert.Null(new PackageCommands(PackageManagerKind.Dnf).SecurityUpdatesAmong([]));
		Assert.Equal(["openssl"], new PackageCommands(PackageManagerKind.Dnf).ParseSecurityUpdates("openssl.x86_64  1:3.0.7-27.el9  baseos\n").Select(update => update.Name));
	}

	private static FirewallStatus UfwStatus() =>
		FirewallCommands.ParseUfwSections("@@mt:verbose\n" + FirewallFixtures.UfwActive.ReplaceLineEndings("\n") + "@@mt:numbered\n" + UfwNumbered.ReplaceLineEndings("\n"));

	private static AccountsSnapshot Snapshot() => new()
	{
		Users =
		[
			new UserAccount { Name = "root", Uid = 0, Gid = 0 },
			new UserAccount { Name = "alice", Uid = 1000, Gid = 1000 },
		],
		Groups = [new GroupAccount("root", 0, []), new GroupAccount("alice", 1000, []), new GroupAccount("sudo", 27, ["alice"]), new GroupAccount("deploy", 1001, [])],
		FirstPersonUid = 1000,
	};
}
