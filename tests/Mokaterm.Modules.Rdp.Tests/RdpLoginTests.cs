using Mokaterm.Modules.Rdp.Protocol;

namespace Mokaterm.Modules.Rdp.Tests;

public sealed class RdpLoginTests
{
	[Fact]
	public void Parse_APlainName_LeavesTheDomainEmpty()
	{
		RdpLogin login = RdpLogin.Parse("alice", null, "secret");

		Assert.Equal("", login.Domain);
		Assert.Equal("alice", login.Username);
		Assert.Equal("secret", login.Password);
	}

	[Fact]
	public void Parse_ADownLevelName_SplitsOffTheDomain()
	{
		RdpLogin login = RdpLogin.Parse("CORP\\alice", null, "secret");

		Assert.Equal("CORP", login.Domain);
		Assert.Equal("alice", login.Username);
	}

	[Fact]
	public void Parse_ADomainInTheOptions_WinsOverTheOneInTheName()
	{
		RdpLogin login = RdpLogin.Parse("CORP\\alice", "OTHER", "secret");

		Assert.Equal("OTHER", login.Domain);
		Assert.Equal("CORP\\alice", login.Username);
	}

	[Fact]
	public void Parse_APrincipalName_StaysWhole()
	{
		RdpLogin login = RdpLogin.Parse("alice@corp.example", null, "secret");

		Assert.Equal("", login.Domain);
		Assert.Equal("alice@corp.example", login.Username);
	}

	[Fact]
	public void Parse_TrimsWhatItIsGiven()
	{
		RdpLogin login = RdpLogin.Parse("  alice  ", "  CORP  ", "secret");

		Assert.Equal("CORP", login.Domain);
		Assert.Equal("alice", login.Username);
	}

	[Fact]
	public void Parse_NoCredentialsAtAll_GivesEmptyStrings()
	{
		RdpLogin login = RdpLogin.Parse(null, null, null);

		Assert.Equal("", login.Domain);
		Assert.Equal("", login.Username);
		Assert.Equal("", login.Password);
	}

	// A record struct's generated ToString prints every field, so a log line or an exception message that formats a login
	// would carry the password with it.
	[Fact]
	public void ToString_KeepsTheNamesButNotThePassword()
	{
		string text = RdpLogin.Parse("CORP\\alice", null, "hunter2-password").ToString();

		Assert.DoesNotContain("hunter2-password", text, StringComparison.Ordinal);
		Assert.Contains("CORP", text, StringComparison.Ordinal);
		Assert.Contains("alice", text, StringComparison.Ordinal);
		Assert.Contains("Password = (set)", text, StringComparison.Ordinal);
		Assert.Contains("Password = (empty)", RdpLogin.Parse("alice", null, null).ToString(), StringComparison.Ordinal);
	}

	[Fact]
	public void Parse_ANameThatStartsWithABackslash_KeepsIt()
	{
		RdpLogin login = RdpLogin.Parse("\\alice", null, "");

		Assert.Equal("", login.Domain);
		Assert.Equal("\\alice", login.Username);
	}
}
