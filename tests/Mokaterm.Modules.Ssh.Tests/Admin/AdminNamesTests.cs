using Mokaterm.Modules.Ssh.Admin;

namespace Mokaterm.Modules.Ssh.Tests.Admin;

public sealed class AdminNamesTests
{
	[Theory]
	[InlineData("alice", true)]
	[InlineData("_apt", true)]
	[InlineData("www-data", true)]
	[InlineData("first.last", true)]
	[InlineData("Admin1", true)]
	[InlineData("a2345678901234567890123456789012", true)]
	[InlineData("a23456789012345678901234567890123", false)]
	[InlineData("-rf", false)]
	[InlineData("1user", false)]
	[InlineData("al ice", false)]
	[InlineData("al:ice", false)]
	[InlineData("al'ice", false)]
	[InlineData("", false)]
	[InlineData(null, false)]
	public void Accounts(string? name, bool valid) => Assert.Equal(valid, AdminNames.CheckAccount(name) is null);

	[Theory]
	[InlineData("nginx", true)]
	[InlineData("nginx.service", true)]
	[InlineData("getty@tty1.service", true)]
	[InlineData(@"systemd-fsck@dev-disk-by\x2duuid-1.service", true)]
	[InlineData("dbus-org.freedesktop.network1.service", true)]
	[InlineData("-nginx", false)]
	[InlineData("nginx service", false)]
	[InlineData("nginx;reboot", false)]
	[InlineData("../../bin/sh", false)]
	[InlineData("$(id)", false)]
	public void Services(string name, bool valid) => Assert.Equal(valid, AdminNames.CheckService(name) is null);

	[Theory]
	[InlineData("htop", true)]
	[InlineData("libstdc++6", true)]
	[InlineData("python3.12", true)]
	[InlineData("libc6:amd64", true)]
	[InlineData("perl-Net-SSLeay", true)]
	[InlineData("@development-tools", false)]
	[InlineData("-y", false)]
	[InlineData("pkg name", false)]
	[InlineData("pkg>1.0", false)]
	[InlineData("*", false)]
	public void Packages(string name, bool valid) => Assert.Equal(valid, AdminNames.CheckPackage(name) is null);

	[Theory]
	[InlineData("UTC", true)]
	[InlineData("Europe/Berlin", true)]
	[InlineData("America/Argentina/Buenos_Aires", true)]
	[InlineData("Etc/GMT+5", true)]
	[InlineData("Etc/GMT-14", true)]
	[InlineData("../etc/passwd", false)]
	[InlineData("Europe/..", false)]
	[InlineData("/etc/localtime", false)]
	[InlineData("Europe/Berlin;id", false)]
	[InlineData("Europe//Berlin", false)]
	[InlineData("a/b/c/d", false)]
	public void TimeZones(string zone, bool valid) => Assert.Equal(valid, AdminNames.CheckTimeZone(zone) is null);

	[Theory]
	[InlineData("eth0", true)]
	[InlineData("enp0s31f6", true)]
	[InlineData("eth0.100", true)]
	[InlineData("br-1a2b3c4d5e6f", true)]
	[InlineData("wg0", true)]
	[InlineData("eth0:1", true)]
	[InlineData("a234567890123456", false)]
	[InlineData("eth0/1", false)]
	[InlineData("eth 0", false)]
	[InlineData("-eth0", false)]
	public void Interfaces(string name, bool valid) => Assert.Equal(valid, AdminNames.CheckInterface(name) is null);

	[Theory]
	[InlineData("web-01", true)]
	[InlineData("web-01.example.com", true)]
	[InlineData("a", true)]
	[InlineData("-web", false)]
	[InlineData("web-", false)]
	[InlineData("web_01", false)]
	[InlineData("web..example", false)]
	[InlineData("we b", false)]
	[InlineData("", false)]
	public void HostNames(string name, bool valid) => Assert.Equal(valid, AdminNames.CheckHostName(name) is null);

	[Fact]
	public void HostNames_LabelsAndLength()
	{
		Assert.Null(AdminNames.CheckHostName(new string('a', 63)));
		Assert.NotNull(AdminNames.CheckHostName(new string('a', 64)));
		Assert.NotNull(AdminNames.CheckHostName(string.Join('.', Enumerable.Repeat(new string('a', 63), 5))));
	}

	[Theory]
	[InlineData("/var/log/syslog", true)]
	[InlineData("/var/log/nginx/error.log", true)]
	[InlineData("/var/log/apt/history.log", true)]
	[InlineData("/var/log/../../etc/shadow", false)]
	[InlineData("/var/log/./syslog", false)]
	[InlineData("/var/logs/x", false)]
	[InlineData("/etc/passwd", false)]
	[InlineData("/var/log/my log", false)]
	[InlineData("/var/log/", false)]
	public void LogPaths(string path, bool valid) => Assert.Equal(valid, AdminNames.CheckLogPath(path) is null);

	[Theory]
	[InlineData("Alice Liddell", true)]
	[InlineData("", true)]
	[InlineData(null, true)]
	[InlineData("Smith, John", false)]
	[InlineData("a:b", false)]
	[InlineData("tab\there", false)]
	public void Comments(string? comment, bool valid) => Assert.Equal(valid, AdminNames.CheckComment(comment) is null);

	[Theory]
	[InlineData("correct horse battery staple", true)]
	[InlineData("p@ss:word'\"$`", true)]
	[InlineData("two\nlines", false)]
	[InlineData("carriage\rreturn", false)]
	[InlineData("", false)]
	public void Passwords(string password, bool valid) => Assert.Equal(valid, AdminNames.CheckPassword(password) is null);

	[Theory]
	[InlineData("/bin/bash", true)]
	[InlineData("/usr/bin/zsh", true)]
	[InlineData("bash", false)]
	[InlineData("/bin/sh -c id", false)]
	[InlineData("/bin/sh;id", false)]
	public void Shells(string shell, bool valid) => Assert.Equal(valid, AdminNames.CheckShellPath(shell) is null);
}
