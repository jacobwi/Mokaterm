using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Rdp.Tests.Fakes;

namespace Mokaterm.Modules.Rdp.Tests;

/// <summary>
/// Runs against a real server when MOKATERM_TEST_RDP is set to <c>user:password@host:port</c>, with the port
/// optional. Percent-encode special characters in the login (<c>%5C</c> for a backslash, <c>%40</c> for an at sign),
/// add <c>?domain=CORP</c> for a domain account and <c>?nla=false</c> to leave the login to the server's own screen.
/// It connects, waits for the first picture, sends a harmless key and disconnects.
/// </summary>
public sealed class RdpLiveServerTests
{
	private const string Variable = "MOKATERM_TEST_RDP";

	[Fact(Timeout = RdpHarness.TestTimeoutMilliseconds)]
	public async Task Connect_PaintsTheDesktopAndTakesInput()
	{
		string setting = LiveEndpoint.Require(Variable, $"Set {Variable}=user:password@host:port to run against a real RDP server.");

		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		LiveEndpoint target = LiveEndpoint.Parse(setting, RdpProtocolProvider.DefaultPort);
		FakeCredentialSource credentials = new(target.User, target.Password);
		FakeHostVerifier verifier = FakeHostVerifier.Accepting();
		ProtocolConnectContext context = RdpHarness.CreateContext(
			target.Port,
			credentials,
			verifier,
			RdpConnectionOptions.Default with
			{
				Domain = target.Option("domain") ?? "",
				NetworkLevelAuthentication = target.Flag("nla", fallback: true),
			},
			address: target.Host);

		await using IProtocolSession session = await RdpHarness.CreateProvider().ConnectAsync(context, cancellationToken);
		IRdpConnection connection = session.GetFeature<IRdpConnection>()
			?? throw new InvalidOperationException("An RDP session must expose IRdpConnection.");

		Assert.True(connection.Info.Width > 0);
		Assert.True(connection.Info.Height > 0);
		Assert.True(connection.Info.IsEncrypted);
		Assert.NotEmpty(verifier.Seen);

		using RecordingSink sink = new();
		await using IRdpChannel channel = await connection.AttachAsync(sink, cancellationToken);
		await channel.StartAsync(cancellationToken);
		await sink.WaitForFramesAsync(1, cancellationToken);

		Assert.True(sink.Frames.TryDequeue(out byte[]? frame));
		Assert.NotNull(frame);
		Assert.True(frame.Length > 12);

		// Moving the pointer and pressing a harmless key must not upset the session.
		await channel.SendInputAsync(
		[
			RdpInputEvent.MouseMove(10, 10),
			RdpInputEvent.KeyDown("ShiftLeft"),
			RdpInputEvent.KeyUp("ShiftLeft"),
		], cancellationToken);

		Assert.False(session.Completion.IsCompleted);
	}
}
