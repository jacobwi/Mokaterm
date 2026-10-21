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
	[InlineData("microsoftaccount\\you@example.com", null)]
	public void CredsspRefusal_AMicrosoftAccount_SaysBothWaysThatWork(string username, string? domain)
	{
		string? refusal = RdpLogin.Parse(username, domain, "secret").CredsspRefusal;

		Assert.Equal(
			"Network level authentication from this client does not work for a Microsoft account. To sign in with it, turn off " +
			"\"Require devices to use Network Level Authentication to connect\" on that PC (Settings > System > Remote Desktop) " +
			"and \"Network level authentication\" in this login's RDP options. The login then signs in by itself. Or keep " +
			"network level authentication, run whoami on that PC and use the name it prints (such as DESKTOP-ABC\\name). " +
			"Either way, use the account's password, not its PIN.",
			refusal);
	}

	[Theory]
	[InlineData("AzureAD\\you@company.com", null)]
	[InlineData("you@company.com", "AzureAD")]
	[InlineData("azuread\\you@company.com", "CORP")]
	public void CredsspRefusal_AnEntraIdAccount_SaysToTurnNlaOffOnThePcAndHere(string username, string? domain)
	{
		RdpLogin login = RdpLogin.Parse(username, domain, "secret");

		Assert.True(login.IsEntraId);
		Assert.Equal(
			"A Microsoft Entra ID account cannot pass network level authentication from a PC that is not joined to Entra ID. " +
			"To sign in with it, turn off \"Require devices to use Network Level Authentication to connect\" on that PC " +
			"(Settings > System > Remote Desktop) and \"Network level authentication\" in this login's RDP options. The login " +
			"then signs in by itself.",
			login.CredsspRefusal);
	}

	// Entra ID signs in with the whole work email, which a short name does not carry.
	[Fact]
	public void CredsspRefusal_AnEntraIdAccountWithoutItsEmail_SaysToWriteItWhole()
	{
		string? refusal = RdpLogin.Parse("AzureAD\\you", null, "secret").CredsspRefusal;

		Assert.NotNull(refusal);
		Assert.Contains("It also signs in with its full work email, written AzureAD\\you@company.com.", refusal, StringComparison.Ordinal);
		Assert.Contains("Require devices to use Network Level Authentication to connect", refusal, StringComparison.Ordinal);
	}

	[Fact]
	public void CredsspRefusal_ADomainBesideAPrincipalName_SaysToLeaveOneOut()
	{
		string? refusal = RdpLogin.Parse("alice@corp.example", "CORP", "secret").CredsspRefusal;

		Assert.Equal(
			"Network level authentication in this client cannot send CORP\\alice@corp.example: it has no form for a domain " +
			"together with an @ in the name. Leave the domain empty for a name with an @ in it, or write it as DOMAIN\\name.",
			refusal);
	}

	[Theory]
	[InlineData("alice", null)]
	[InlineData("CORP\\alice", null)]
	[InlineData("MicrosoftAccount\\you", null)]
	public void CloudAccountAdvice_IsOnlyForEntraIdAndMicrosoftAccounts(string username, string? domain)
	{
		RdpLogin login = RdpLogin.Parse(username, domain, null);

		Assert.Equal(login.IsMicrosoftAccount, login.CloudAccountAdvice is not null);
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
