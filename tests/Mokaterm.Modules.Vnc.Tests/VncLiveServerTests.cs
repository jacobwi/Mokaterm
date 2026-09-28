using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Vnc.Tests.Fakes;
using Mokaterm.Modules.Vnc.Tests.Loopback;

namespace Mokaterm.Modules.Vnc.Tests;

/// <summary>
/// Runs against a real server when MOKATERM_TEST_VNC is set to <c>password@host:port</c>, with the port and the
/// password optional (<c>host</c> alone means display :0 without a password). Percent-encode special characters in
/// the password, and add <c>?encryption=Required</c> to insist on VeNCrypt with TLS.
/// </summary>
public sealed class VncLiveServerTests
{
	private const string Variable = "MOKATERM_TEST_VNC";

	[Fact(Timeout = VncHarness.TestTimeoutMilliseconds)]
	public async Task Connect_Handshake_AndRelayTheFirstScreenUpdate()
	{
		string setting = LiveEndpoint.Require(Variable, $"Set {Variable}=password@host:port to run against a real VNC server.");

		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		LiveEndpoint target = LiveEndpoint.Parse(setting, VncProtocolProvider.DefaultPort);

		// A lone token in the login is the password: a VNC server authenticates the screen, not a user.
		string? user = target.Password is null ? null : target.User;
		string? password = target.Password ?? target.User;
		FakeCredentialSource credentials = new(
			user,
			password,
			password is null ? AuthenticationMethod.Anonymous : AuthenticationMethod.Password);
		FakeHostVerifier verifier = FakeHostVerifier.Accepting();
		ProtocolConnectContext context = VncHarness.CreateContext(
			target.Port,
			credentials,
			verifier,
			VncConnectionOptions.Default with { Encryption = target.Option("encryption", VncEncryptionMode.Preferred) },
			address: target.Host);

		(IProtocolSession session, IVncConnection connection) = await VncHarness.ConnectAsync(
			VncHarness.CreateProvider(),
			context,
			cancellationToken);
		await using (session)
		{
			Assert.True(connection.Info.Width > 0);
			Assert.True(connection.Info.Height > 0);

			using RecordingSink sink = new();
			await using IVncChannel channel = await connection.AttachAsync(sink, cancellationToken);
			await channel.StartAsync(cancellationToken);
			await sink.WaitForChunksAsync(1, cancellationToken);
			await channel.SendAsync(VncHarness.PageHandshake(), cancellationToken);
			await sink.WaitForChunksAsync(4, cancellationToken);

			// What noVNC sends next: 32 bit true colour, the Raw encoding only, then a full screen update.
			byte[] setPixelFormat = [0, 0, 0, 0, 32, 24, 0, 1, 0, 255, 0, 255, 0, 255, 16, 8, 0, 0, 0, 0];
			byte[] setEncodings = [2, 0, 0, 1, 0, 0, 0, 0];
			byte[] update =
			[
				3, 0, 0, 0, 0, 0,
				(byte)(connection.Info.Width >> 8), (byte)connection.Info.Width,
				(byte)(connection.Info.Height >> 8), (byte)connection.Info.Height,
			];
			await channel.SendAsync(setPixelFormat, cancellationToken);
			await channel.SendAsync(setEncodings, cancellationToken);
			await channel.SendAsync(update, cancellationToken);

			int handshakeBytes = sink.Bytes.Length;
			await sink.WaitForBytesAsync(handshakeBytes + 16, cancellationToken);

			// A FramebufferUpdate starts with message type 0.
			Assert.Equal(0, sink.Bytes[handshakeBytes]);
		}
	}
}
