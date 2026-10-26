using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Security;
using Mokaterm.Modules.Rdp.Protocol;
using Mokaterm.Modules.Rdp.Tests.Fakes;

namespace Mokaterm.Modules.Rdp.Tests;

public sealed class RdpKerberosRealmTests
{
	[Theory]
	[InlineData("CORP", "pc.corp.example.com", "corp.example.com")]
	[InlineData("corp", "PC.CORP.example.com", "CORP.example.com")]
	[InlineData("EXAMPLE", "pc.corp.example.com", "example.com")]
	[InlineData("CORP", "pc.corp.example.com.", "corp.example.com")]
	public void FromHostName_ADomainInThePcsName_IsItsDnsName(string domain, string host, string expected) =>
		Assert.Equal(expected, RdpKerberosRealm.FromHostName(domain, host)?.DnsName);

	[Theory]
	[InlineData("CORP", "pc")]
	[InlineData("CORP", "pc.other.example.com")]
	[InlineData("COM", "pc.corp.com")]
	[InlineData("CORP", "10.0.0.5")]
	[InlineData("CORP", "fe80::1")]
	public void FromHostName_NoMatchingPart_FindsNothing(string domain, string host) =>
		Assert.Null(RdpKerberosRealm.FromHostName(domain, host));

	[Theory]
	[InlineData("CORP", "pc", true)]
	[InlineData("CORP", "10.0.0.5", true)]
	[InlineData("corp.example.com", "pc", false)]
	[InlineData("", "pc", false)]
	[InlineData(".", "pc", false)]
	[InlineData("AzureAD", "pc", false)]
	[InlineData("MicrosoftAccount", "pc", false)]
	[InlineData("PC1", "pc1.corp.example.com", false)]
	[InlineData("PC1", "PC1", false)]
	public void IsShortDomain_OnlyADomainControllersShortName(string domain, string host, bool expected) =>
		Assert.Equal(expected, RdpKerberosRealm.IsShortDomain(domain, host));

	[Fact]
	public async Task FindAsync_ThisMachine_IsNeverLookedUp()
	{
		Assert.Null(await RdpKerberosRealm.FindAsync("CORP", "127.0.0.1", TestContext.Current.CancellationToken));
		Assert.Null(await RdpKerberosRealm.FindAsync("CORP", "localhost", TestContext.Current.CancellationToken));
	}

	[Fact(Timeout = RdpHarness.TestTimeoutMilliseconds)]
	public async Task Connect_AShortDomainWithAKnownDnsName_SignsInWithTheDnsName()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using FakeRdpServer server = new FakeRdpServer(FakeRdpServer.ProtocolHybrid)
		{
			Then = static _ => FakeRdpServer.AfterTls.RefuseLogin,
		}.Start();
		using LoginCredentials credentials = Login("CORP\\alice");
		RecordingProgress<string> trace = new();
		RdpConnector connector = new(
			"127.0.0.1",
			server.Port,
			RdpConnectionOptions.Default,
			TimeSpan.FromSeconds(5),
			FakeHostVerifier.Accepting(),
			realms: static (domain, _, _) => Task.FromResult<RdpRealm?>(domain == "CORP" ? new RdpRealm("corp.example.com", "a test") : null))
		{
			Trace = trace,
		};

		await Assert.ThrowsAsync<RdpAuthenticationException>(() => connector.ConnectAsync(credentials, null, cancellationToken));

		Assert.Equal(("corp.example.com", "alice"), Assert.Single(server.Logins));
		Assert.Contains("Domain CORP is corp.example.com, from a test", trace.Reports);
	}

	[Fact(Timeout = RdpHarness.TestTimeoutMilliseconds)]
	public async Task Connect_WithoutNla_KeepsTheDomainAsWritten()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		bool asked = false;
		await using FakeRdpServer server = new FakeRdpServer(FakeRdpServer.ProtocolSsl).Start();
		using LoginCredentials credentials = Login("CORP\\alice");
		RdpConnector connector = new(
			"127.0.0.1",
			server.Port,
			RdpConnectionOptions.Default with { NetworkLevelAuthentication = false },
			TimeSpan.FromSeconds(1.5),
			FakeHostVerifier.Accepting(),
			realms: (_, _, _) =>
			{
				asked = true;
				return Task.FromResult<RdpRealm?>(null);
			});

		await Assert.ThrowsAnyAsync<Exception>(() => connector.ConnectAsync(credentials, null, cancellationToken));

		Assert.False(asked);
	}

	private static LoginCredentials Login(string username) => new()
	{
		Username = username,
		Method = AuthenticationMethod.Password,
		Password = SecretBuffer.FromString(RdpHarness.Password),
	};
}
