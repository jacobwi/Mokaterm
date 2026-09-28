using Microsoft.Extensions.Logging.Abstractions;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Security;
using Mokaterm.Modules.Vnc.Tests.Fakes;

namespace Mokaterm.Modules.Vnc.Tests.Loopback;

/// <summary>Builds servers, providers and connect contexts for the loopback tests.</summary>
internal static class VncHarness
{
	public const string Password = "s3cret";

	public const string User = "alice";

	public const int TestTimeoutMilliseconds = TestTimeouts.NetworkMilliseconds;

	public static LoopbackVncServer StartServer(LoopbackVncServerOptions? options = null) =>
		new(options ?? new LoopbackVncServerOptions());

	public static VncProtocolProvider CreateProvider(FakeSettingsService? settings = null) =>
		new(settings ?? new FakeSettingsService(), NullLoggerFactory.Instance);

	public static ProtocolConnectContext CreateContext(
		int port,
		ICredentialSource? credentials = null,
		IHostIdentityVerifier? verifier = null,
		VncConnectionOptions? options = null,
		RecordingProgress<string>? status = null,
		string address = "127.0.0.1") =>
		new ConnectContext
		{
			ProtocolId = VncProtocolProvider.ProtocolId,
			Port = port,
			Address = address,
			Credentials = credentials ?? new FakeCredentialSource(null, Password),
			Verifier = verifier ?? FakeHostVerifier.Accepting(),
			Options = (options ?? VncConnectionOptions.Default).ApplyTo(ProtocolOptions.Empty),
			Status = status,
		}.Build();

	/// <summary>Opens a session and returns it with its VNC feature.</summary>
	public static async Task<(IProtocolSession Session, IVncConnection Connection)> ConnectAsync(
		VncProtocolProvider provider,
		ProtocolConnectContext context,
		CancellationToken cancellationToken)
	{
		IProtocolSession session = await provider.ConnectAsync(context, cancellationToken);
		IVncConnection connection = session.GetFeature<IVncConnection>()
			?? throw new InvalidOperationException("A VNC session must expose IVncConnection.");
		return (session, connection);
	}

	/// <summary>The fourteen bytes noVNC sends during the synthetic handshake: version, security type and the shared flag.</summary>
	public static byte[] PageHandshake(bool shared = true) =>
		[.. "RFB 003.008\n"u8, 1, shared ? (byte)1 : (byte)0];
}
