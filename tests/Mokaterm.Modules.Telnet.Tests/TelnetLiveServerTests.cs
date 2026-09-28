using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.Modules.Telnet.Tests.Fakes;
using Mokaterm.Modules.Telnet.Tests.Loopback;

namespace Mokaterm.Modules.Telnet.Tests;

/// <summary>
/// Runs against a real device when MOKATERM_TEST_TELNET is set to <c>user:password@host:port</c>, with the login
/// and the port optional (<c>host</c> alone connects to port 23 and types nothing). A login turns the automatic
/// login on, so the test also covers typing it at the device's own prompt.
/// </summary>
public sealed class TelnetLiveServerTests
{
	private const string Variable = "MOKATERM_TEST_TELNET";

	[Fact(Timeout = TelnetHarness.TestTimeoutMilliseconds)]
	public async Task Connect_NegotiatesAndReadsWhateverTheDeviceSends()
	{
		string setting = LiveEndpoint.Require(Variable, $"Set {Variable}=user:password@host:port to run against a real telnet server.");

		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		LiveEndpoint target = LiveEndpoint.Parse(setting, TelnetProtocolProvider.DefaultPort);
		FakeCredentialSource credentials = new(
			target.User,
			target.Password,
			target.User is null ? AuthenticationMethod.Anonymous : AuthenticationMethod.Password);
		ProtocolConnectContext context = TelnetHarness.CreateContext(
			target.Port,
			TelnetConnectionOptions.Default with { AutoLogin = target.User is not null },
			credentials,
			address: target.Host);

		(IProtocolSession session, ITerminalChannel terminal) = await TelnetHarness.ConnectAsync(
			TelnetHarness.CreateProvider(),
			context,
			cancellationToken);

		await using (session)
		{
			// Any device says something: a banner, a login prompt or the shell prompt itself.
			byte[] buffer = new byte[4096];
			int read = await terminal.ReadAsync(buffer, cancellationToken);
			Assert.True(read > 0);

			await terminal.ResizeAsync(new TerminalSize(90, 30), cancellationToken);
			await terminal.WriteAsync("\r"u8.ToArray(), cancellationToken);
			Assert.True(await terminal.ReadAsync(buffer, cancellationToken) > 0);
		}
	}
}
