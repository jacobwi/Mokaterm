using Mokaterm.Abstractions.ServerAdmin;
using Mokaterm.Plugins.Sample.Fail2ban;

namespace Mokaterm.Plugins.Sample.Tests;

/// <summary>The fail2ban page's scripts: one read for every jail, an unban that only takes what fail2ban could have listed.</summary>
public sealed class Fail2banCommandsTests
{
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void Status_ReadsEveryJail_InOneScript(bool asRoot)
	{
		SystemScript script = Fail2banCommands.Status(asRoot);

		Assert.True(SystemScript.IsName(script.Name));
		Assert.Equal(asRoot, script.AsRoot);
		Assert.Contains("fail2ban-client status", script.Body, StringComparison.Ordinal);
		Assert.Contains("set -f", script.Body, StringComparison.Ordinal);
		Assert.Contains(Fail2banCommands.JailMarker.Trim(), script.Body, StringComparison.Ordinal);
	}

	[Fact]
	public void Unban_RunsAsRoot_WithEveryValueQuoted()
	{
		SystemChange change = Fail2banCommands.Unban("sshd", "198.51.100.23");

		Assert.Equal("Unban 198.51.100.23 in sshd", change.Title);
		Assert.Equal("Unban", change.ConfirmText);
		Assert.True(change.Script.AsRoot);
		Assert.True(SystemScript.IsName(change.Script.Name));
		Assert.Equal("fail2ban-client set 'sshd' unbanip '198.51.100.23'", change.Script.Body);
		Assert.Null(change.Input);
	}

	[Theory]
	[InlineData("sshd", "2001:db8:4::1f")]
	[InlineData("nginx-http-auth", "203.0.113.0/24")]
	[InlineData("recidive", "2001:db8::/48")]
	public void Unban_TakesWhatFail2banLists(string jail, string address) =>
		Assert.Contains($"unbanip '{address}'", Fail2banCommands.Unban(jail, address).Script.Body, StringComparison.Ordinal);

	[Theory]
	[InlineData("sshd; reboot", "198.51.100.23")]
	[InlineData("-sshd", "198.51.100.23")]
	[InlineData("ssh d", "198.51.100.23")]
	[InlineData("sshd", "10.1")]
	[InlineData("sshd", "167772161")]
	[InlineData("sshd", "198.51.100.23; reboot")]
	[InlineData("sshd", "198.51.100.23/33")]
	[InlineData("sshd", "fe80::1%eth0")]
	[InlineData("sshd", "")]
	[InlineData("sshd", "-h")]
	public void Unban_RefusesAnythingElse(string jail, string address) =>
		Assert.Throws<ArgumentException>(() => Fail2banCommands.Unban(jail, address));

	[Fact]
	public void TheRule_GreysThePage_WithoutTheClient()
	{
		ServerFacts without = new() { DisplayName = "Debian 12" };
		ServerFacts with = without with { Commands = new HashSet<string>(StringComparer.Ordinal) { Fail2banCommands.Client } };

		Assert.NotNull(Fail2banCommands.Unavailable(without));
		Assert.Null(Fail2banCommands.Unavailable(with));
	}

	[Fact]
	public void ASocketThatTurnedTheLoginAway_IsAskedAsRoot()
	{
		SystemScriptResult refused = new() { ExitCode = 255, Error = "ERROR  Permission denied to socket: /var/run/fail2ban/fail2ban.sock, (you must be root)" };
		SystemScriptResult down = new() { ExitCode = 255, Error = "ERROR  Failed to access socket path: /var/run/fail2ban/fail2ban.sock. Is fail2ban running?" };

		Assert.True(Fail2banCommands.NeedsRoot(refused));
		Assert.False(Fail2banCommands.NeedsRoot(down));
		Assert.False(Fail2banCommands.NeedsRoot(new SystemScriptResult { ExitCode = 0 }));
	}
}
