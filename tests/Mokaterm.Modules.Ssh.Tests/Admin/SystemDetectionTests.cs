using Mokaterm.Modules.Ssh.Admin;
using Mokaterm.Modules.Ssh.Admin.Detection;

namespace Mokaterm.Modules.Ssh.Tests.Admin;

public sealed class SystemDetectionTests
{
	[Fact]
	public void Debian_IsAptOnSystemdWithAnIdleUfw()
	{
		SystemProfile profile = Detect(DetectionFixtures.Debian12);

		Assert.Equal(PlatformKind.Linux, profile.Platform);
		Assert.Equal(DistroFamily.Debian, profile.Family);
		Assert.Equal(InitSystem.Systemd, profile.Init);
		Assert.Equal(PackageManagerKind.Apt, profile.PackageManager);
		Assert.Equal(FirewallKind.Ufw, profile.Firewall);
		Assert.Equal(AccountToolKind.Shadow, profile.AccountTools);
		Assert.False(profile.IsContainer);
		Assert.Null(profile.UnsupportedReason);
		Assert.Equal("Debian GNU/Linux 12 (bookworm)", profile.DisplayName);
		Assert.Equal("Linux 6.1.0-25-amd64 x86_64", profile.Facts.Kernel);
		Assert.Equal("alice", profile.Facts.UserName);
		Assert.False(profile.Facts.IsRoot);
		Assert.Equal("10.0.0.5", profile.Facts.SshServerAddress);
		Assert.Equal(22, profile.Facts.SshServerPort);
		Assert.False(profile.Facts.UfwEnabled);
		Assert.True(profile.HasJournal);
		Assert.True(profile.Capabilities.Clock.UsesTimedatectl);
		Assert.True(profile.Capabilities.HostName.UsesHostnamectl);
		Assert.True(profile.Capabilities.HasTimers);
		Assert.True(profile.Capabilities.HasCrontab);
	}

	[Fact]
	public void Ubuntu_AsRoot_ReadsTheMappedAddressAndTheEnabledUfw()
	{
		SystemProfile profile = Detect(DetectionFixtures.Ubuntu2404);

		Assert.Equal(DistroFamily.Debian, profile.Family);
		Assert.Equal(FirewallKind.Ufw, profile.Firewall);
		Assert.True(profile.Facts.UfwEnabled);
		Assert.True(profile.Facts.IsRoot);
		Assert.Equal("::ffff:10.1.2.3", profile.Facts.SshServerAddress);
		Assert.Equal(2222, profile.Facts.SshServerPort);
		Assert.Equal(2222, profile.Capabilities.Firewall!.SshPort);
	}

	[Fact]
	public void Rocky_IsDnfWithTheActiveFirewalld()
	{
		SystemProfile profile = Detect(DetectionFixtures.Rocky9);

		Assert.Equal(DistroFamily.RedHat, profile.Family);
		Assert.Equal(InitSystem.Systemd, profile.Init);
		Assert.Equal(PackageManagerKind.Dnf, profile.PackageManager);
		Assert.Equal(FirewallKind.Firewalld, profile.Firewall);
		Assert.Equal("Rocky Linux 9.4 (Blue Onyx)", profile.DisplayName);
		Assert.Equal(["rhel", "centos", "fedora"], profile.Facts.OsRelease.IdLike);
	}

	[Fact]
	public void Fedora_IsDnf()
	{
		SystemProfile profile = Detect(DetectionFixtures.Fedora40);

		Assert.Equal(DistroFamily.RedHat, profile.Family);
		Assert.Equal(PackageManagerKind.Dnf, profile.PackageManager);
		Assert.Equal(FirewallKind.Firewalld, profile.Firewall);
		Assert.False(profile.Capabilities.HasCrontab);
	}

	[Fact]
	public void CentOs7_WithoutDnf_IsYum()
	{
		SystemProfile profile = Detect(DetectionFixtures.CentOs7);

		Assert.Equal(PackageManagerKind.Yum, profile.PackageManager);

		// Installed and idle, firewalld is still the distribution's own front end.
		Assert.Equal(FirewallKind.Firewalld, profile.Firewall);
	}

	[Fact]
	public void OpenSuse_IsZypper()
	{
		SystemProfile profile = Detect(DetectionFixtures.OpenSuseLeap);

		Assert.Equal(DistroFamily.Suse, profile.Family);
		Assert.Equal(PackageManagerKind.Zypper, profile.PackageManager);
		Assert.Equal(FirewallKind.Firewalld, profile.Firewall);
	}

	[Fact]
	public void Arch_IsPacmanWithNftables()
	{
		SystemProfile profile = Detect(DetectionFixtures.Arch);

		Assert.Equal(DistroFamily.Arch, profile.Family);
		Assert.Equal(PackageManagerKind.Pacman, profile.PackageManager);
		Assert.Equal(FirewallKind.Nftables, profile.Firewall);
		Assert.False(profile.Capabilities.Firewall!.CanChange);
		Assert.False(profile.Capabilities.Packages!.HasRefresh);
	}

	[Fact]
	public void Alpine_IsApkOnOpenRcWithBusyBoxAccounts()
	{
		SystemProfile profile = Detect(DetectionFixtures.Alpine);

		Assert.Equal(DistroFamily.Alpine, profile.Family);
		Assert.Equal(InitSystem.OpenRc, profile.Init);
		Assert.Equal(PackageManagerKind.Apk, profile.PackageManager);
		Assert.Equal(FirewallKind.Iptables, profile.Firewall);
		Assert.Equal(AccountToolKind.BusyBox, profile.AccountTools);
		Assert.False(profile.HasJournal);
		Assert.False(profile.Capabilities.Clock.UsesTimedatectl);
		Assert.False(profile.Capabilities.HasTimers);
		Assert.False(profile.IsContainer);
	}

	[Fact]
	public void AlpineContainer_HasNoInitAndSaysSo()
	{
		SystemProfile profile = Detect(DetectionFixtures.AlpineContainer);

		Assert.Equal(InitSystem.None, profile.Init);
		Assert.True(profile.IsContainer);
		Assert.Null(profile.Capabilities.Services);
		Assert.Contains("container", profile.Capabilities.ServicesMissing, StringComparison.Ordinal);
		Assert.Equal(PackageManagerKind.Apk, profile.PackageManager);
		Assert.Equal(FirewallKind.None, profile.Firewall);
		Assert.Null(profile.Capabilities.Firewall);
		Assert.Null(profile.UnsupportedReason);
	}

	[Fact]
	public void Devuan_IsSysVInitWithUpdateRcD()
	{
		SystemProfile profile = Detect(DetectionFixtures.DevuanSysV);

		Assert.Equal(DistroFamily.Debian, profile.Family);
		Assert.Equal(InitSystem.SysV, profile.Init);
		Assert.Equal(PackageManagerKind.Apt, profile.PackageManager);
		Assert.True(profile.Capabilities.Services!.CanEnable);
		Assert.False(profile.Capabilities.Clock.UsesTimedatectl);
		Assert.False(profile.HasJournal);
	}

	[Fact]
	public void MacOs_IsNotSupported_AndNamesItself()
	{
		SystemProfile profile = Detect(DetectionFixtures.MacOs);

		Assert.Equal(PlatformKind.MacOs, profile.Platform);
		Assert.NotNull(profile.UnsupportedReason);
		Assert.Equal("macOS 14.4.1", profile.DisplayName);
		Assert.Equal(PackageManagerKind.None, profile.PackageManager);
		Assert.Equal(InitSystem.None, profile.Init);
	}

	[Fact]
	public void FreeBsd_IsNotSupported()
	{
		SystemProfile profile = Detect(DetectionFixtures.FreeBsd);

		Assert.Equal(PlatformKind.Bsd, profile.Platform);
		Assert.Contains("FreeBSD", profile.UnsupportedReason, StringComparison.Ordinal);
	}

	[Fact]
	public void Parse_IgnoresNamesTheScriptNeverAsks()
	{
		SystemFacts facts = SystemProbe.Parse("@@mt:commands\nrm\nsystemctl\n@@mt:paths\n/etc/passwd\n/etc/crontab\n");

		Assert.Equal("systemctl", Assert.Single(facts.Commands));
		Assert.Equal("/etc/crontab", Assert.Single(facts.Paths));
	}

	[Theory]
	[InlineData("1.2.3.4 5555 10.0.0.1 22", "10.0.0.1", 22)]
	[InlineData("fe80::1%eth0 5555 fe80::2%eth0 2200", "fe80::2%eth0", 2200)]
	[InlineData("", null, null)]
	[InlineData("garbage", null, null)]
	[InlineData("1.2.3.4 5555 10.0.0.1 99999", "10.0.0.1", null)]
	public void SshConnection_GivesTheServerHalf(string value, string? address, int? port)
	{
		(string? parsedAddress, int? parsedPort) = SystemProbe.ParseSshConnection(value);

		Assert.Equal(address, parsedAddress);
		Assert.Equal(port, parsedPort);
	}

	[Fact]
	public void UnknownDistribution_FallsBackToWhicheverManagerIsThere()
	{
		SystemFacts facts = new()
		{
			KernelName = "Linux",
			Commands = new HashSet<string>(["pacman", "systemctl"], StringComparer.Ordinal),
			Paths = new HashSet<string>(["/run/systemd/system"], StringComparer.Ordinal),
			OsRelease = OsRelease.Parse("ID=someos\n"),
		};

		Assert.Equal(PackageManagerKind.Pacman, SystemProfile.From(facts).PackageManager);
	}

	[Theory]
	[InlineData("Linux", nameof(PlatformKind.Linux))]
	[InlineData("Darwin", nameof(PlatformKind.MacOs))]
	[InlineData("FreeBSD", nameof(PlatformKind.Bsd))]
	[InlineData("OpenBSD", nameof(PlatformKind.Bsd))]
	[InlineData("NetBSD", nameof(PlatformKind.Bsd))]
	[InlineData("SunOS", nameof(PlatformKind.Other))]
	[InlineData("MINGW64_NT-10.0-26200", nameof(PlatformKind.Other))]
	public void Platform_ComesFromTheKernelName(string kernel, string expected) =>
		Assert.Equal(Enum.Parse<PlatformKind>(expected), SystemProfile.PlatformOf(kernel));

	[Fact]
	public void OsRelease_ReadsShellQuoting()
	{
		OsRelease release = OsRelease.Parse("""
			# a comment
			NAME="Some \"Quoted\" OS"
			ID='single'
			ID_LIKE="rhel  fedora"
			VERSION_ID=1.2 # trailing comment
			PRETTY_NAME="Dollar \$HOME and back\\slash"
			lower=ignored
			""");

		Assert.Equal("Some \"Quoted\" OS", release.Name);
		Assert.Equal("single", release.Id);
		Assert.Equal(["rhel", "fedora"], release.IdLike);
		Assert.Equal("1.2", release.VersionId);
		Assert.Equal("Dollar $HOME and back\\slash", release.PrettyName);
		Assert.False(release.Values.ContainsKey("lower"));
	}

	[Fact]
	public void Script_ListsEveryProbedCommandAndPath()
	{
		string body = SystemProbe.Script.Body;

		Assert.Contains("for c in " + string.Join(' ', SystemProbe.ProbedCommands) + "; do", body, StringComparison.Ordinal);
		Assert.Contains("for f in " + string.Join(' ', SystemProbe.ProbedPaths) + "; do", body, StringComparison.Ordinal);
		Assert.False(SystemProbe.Script.AsRoot);
	}

	private static SystemProfile Detect(string output) => SystemProfile.From(SystemProbe.Parse(output.ReplaceLineEndings("\n")));
}
