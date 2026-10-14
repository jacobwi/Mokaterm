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
	public void Parse_ADomainInTheName_WinsOverTheOneInTheOptions()
	{
		// Keeping CORP\alice whole beside a domain was a name sspi-rs refuses outright ("mixed username format").
		RdpLogin login = RdpLogin.Parse("CORP\\alice", "OTHER", "secret");

		Assert.Equal("CORP", login.Domain);
		Assert.Equal("alice", login.Username);
		Assert.Null(login.CredsspRefusal);
	}

	[Fact]
	public void Parse_ADomainInTheOptions_AppliesToAPlainName()
	{
		RdpLogin login = RdpLogin.Parse("alice", "CORP", "secret");

		Assert.Equal("CORP", login.Domain);
		Assert.Equal("alice", login.Username);
	}

	[Fact]
	public void Parse_AMachineName_IsADomainLikeAnyOther()
	{
		RdpLogin named = RdpLogin.Parse("DESKTOP-ABC\\alice", null, "secret");
		RdpLogin dotted = RdpLogin.Parse(".\\alice", null, "secret");

		Assert.Equal(("DESKTOP-ABC", "alice"), (named.Domain, named.Username));
		Assert.Equal((".", "alice"), (dotted.Domain, dotted.Username));
	}

	[Theory]
	[InlineData("alice", null)]
	[InlineData("CORP\\alice", null)]
	[InlineData("alice@corp.example", null)]
	[InlineData("alice", "CORP")]
	public void CredsspRefusal_IsNullForEveryFormCredsspCanSend(string username, string? domain) =>
		Assert.Null(RdpLogin.Parse(username, domain, "secret").CredsspRefusal);

	[Theory]
	[InlineData("MicrosoftAccount\\you@example.com", null)]
	[InlineData("you@example.com", "MicrosoftAccount")]
	[InlineData("AzureAD\\you@contoso.com", null)]
	public void CredsspRefusal_AMicrosoftAccountName_SaysWhichNameToUseInstead(string username, string? domain)
	{
		string? refusal = RdpLogin.Parse(username, domain, "secret").CredsspRefusal;

		Assert.NotNull(refusal);
		Assert.Contains("run whoami", refusal, StringComparison.Ordinal);
		Assert.Contains("not its PIN", refusal, StringComparison.Ordinal);
	}

	[Fact]
	public void CredsspRefusal_ANameWithTwoBackslashes_SaysHowToWriteOne()
	{
		string? refusal = RdpLogin.Parse("CORP\\alice\\x", null, "secret").CredsspRefusal;

		Assert.Equal("CORP\\alice\\x is not a user name Windows can read. Write it as name, DOMAIN\\name or name@domain.", refusal);
	}

	[Fact]
	public void IsBarePrincipalName_IsOnlyAnAddressWithNoDomain()
	{
		Assert.True(RdpLogin.Parse("you@example.com", null, null).IsBarePrincipalName);
		Assert.False(RdpLogin.Parse("alice", null, null).IsBarePrincipalName);
		Assert.False(RdpLogin.Parse("MicrosoftAccount\\you@example.com", null, null).IsBarePrincipalName);
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
