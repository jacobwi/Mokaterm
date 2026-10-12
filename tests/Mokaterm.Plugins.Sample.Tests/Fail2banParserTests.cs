using Mokaterm.Plugins.Sample.Fail2ban;

namespace Mokaterm.Plugins.Sample.Tests;

/// <summary>What fail2ban-client prints, as the status script hands it over: the jail list, then each jail's tree.</summary>
public sealed class Fail2banParserTests
{
	private const string TwoJails = """
		Status
		|- Number of jail:	2
		`- Jail list:	nginx-http-auth, sshd
		@@f2b:jail nginx-http-auth
		Status for the jail: nginx-http-auth
		|- Filter
		|  |- Currently failed:	0
		|  |- Total failed:	41
		|  `- File list:	/var/log/nginx/error.log
		`- Actions
		   |- Currently banned:	2
		   |- Total banned:	7
		   `- Banned IP list:	203.0.113.77 2001:db8:4::1f
		@@f2b:jail sshd
		Status for the jail: sshd
		|- Filter
		|  |- Currently failed:	2
		|  |- Total failed:	1288
		|  `- Journal matches:	_SYSTEMD_UNIT=ssh.service + _COMM=sshd
		`- Actions
		   |- Currently banned:	1
		   |- Total banned:	93
		   `- Banned IP list:	198.51.100.23
		""";

	[Fact]
	public void Jails_AreReadInOrder_WithTheirCountsAndSources()
	{
		Fail2banStatus status = Fail2banParser.Parse(TwoJails);

		Assert.Equal(["nginx-http-auth", "sshd"], status.JailNames);
		Assert.Equal(["nginx-http-auth", "sshd"], status.Jails.Select(jail => jail.Name));
		Assert.Equal(3, status.BannedNow);

		Fail2banJail nginx = status.Jails[0];
		Assert.Equal(0, nginx.CurrentlyFailed);
		Assert.Equal(41, nginx.TotalFailed);
		Assert.Equal(2, nginx.CurrentlyBanned);
		Assert.Equal(7, nginx.TotalBanned);
		Assert.Equal("/var/log/nginx/error.log", nginx.Source);
		Assert.Equal(["203.0.113.77", "2001:db8:4::1f"], nginx.BannedAddresses);
		Assert.Null(nginx.Problem);

		Fail2banJail sshd = status.Jails[1];
		Assert.Equal("_SYSTEMD_UNIT=ssh.service + _COMM=sshd", sshd.Source);
		Assert.Equal(["198.51.100.23"], sshd.BannedAddresses);
	}

	[Fact]
	public void CarriageReturns_DoNotChangeTheAnswer()
	{
		Fail2banStatus status = Fail2banParser.Parse(TwoJails.ReplaceLineEndings("\r\n"));

		Assert.Equal(["198.51.100.23"], status.Jails[1].BannedAddresses);
		Assert.Equal(1288, status.Jails[1].TotalFailed);
	}

	[Fact]
	public void AJailWithNobodyBanned_HasAnEmptyList()
	{
		Fail2banStatus status = Fail2banParser.Parse("""
			Status
			|- Number of jail:	1
			`- Jail list:	recidive
			@@f2b:jail recidive
			Status for the jail: recidive
			|- Filter
			|  |- Currently failed:	0
			|  |- Total failed:	0
			|  `- File list:	/var/log/fail2ban.log
			`- Actions
			   |- Currently banned:	0
			   |- Total banned:	0
			   `- Banned IP list:
			""");

		Fail2banJail jail = Assert.Single(status.Jails);
		Assert.Empty(jail.BannedAddresses);
		Assert.Equal(0, jail.CurrentlyBanned);
		Assert.Equal(0, status.BannedNow);
	}

	[Fact]
	public void AJailFail2banRefused_KeepsWhatItSaid()
	{
		Fail2banStatus status = Fail2banParser.Parse("""
			Status
			`- Jail list:	gone
			@@f2b:jail gone
			Sorry but the jail 'gone' does not exist
			""");

		Fail2banJail jail = Assert.Single(status.Jails);
		Assert.Equal("Sorry but the jail 'gone' does not exist", jail.Problem);
		Assert.Null(jail.CurrentlyBanned);
	}

	[Fact]
	public void NoJails_IsAnEmptyStatus()
	{
		Fail2banStatus status = Fail2banParser.Parse("Status\n|- Number of jail:\t0\n`- Jail list:\t\n");

		Assert.Empty(status.JailNames);
		Assert.Empty(status.Jails);
	}
}
