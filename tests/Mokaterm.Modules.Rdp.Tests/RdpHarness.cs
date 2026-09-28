using Microsoft.Extensions.Logging.Abstractions;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Security;
using Mokaterm.Modules.Rdp.Tests.Fakes;

namespace Mokaterm.Modules.Rdp.Tests;

/// <summary>Builds providers and connect contexts for the tests that reach the network.</summary>
internal static class RdpHarness
{
	public const string User = "alice";

	public const string Password = "s3cret";

	public const int TestTimeoutMilliseconds = TestTimeouts.NetworkMilliseconds;

	public static RdpProtocolProvider CreateProvider(FakeSettingsService? settings = null) =>
		new(settings ?? new FakeSettingsService(), NullLoggerFactory.Instance);

	public static ProtocolConnectContext CreateContext(
		int port,
		ICredentialSource? credentials = null,
		IHostIdentityVerifier? verifier = null,
		RdpConnectionOptions? options = null,
		RecordingProgress<string>? status = null,
		string address = "127.0.0.1") =>
		new ConnectContext
		{
			ProtocolId = RdpProtocolProvider.ProtocolId,
			Port = port,
			Address = address,
			Credentials = credentials ?? new FakeCredentialSource(User, Password),
			Verifier = verifier ?? FakeHostVerifier.Accepting(),
			Options = (options ?? RdpConnectionOptions.Default).ApplyTo(ProtocolOptions.Empty),
			Status = status,
		}.Build();

	/// <summary>A picture whose pixels are worked out from their position, so a wrong row or column shows up.</summary>
	public static byte[] Gradient(int width, int height)
	{
		byte[] pixels = new byte[width * height * 4];
		for (int y = 0; y < height; y++)
		{
			for (int x = 0; x < width; x++)
			{
				int offset = ((y * width) + x) * 4;
				pixels[offset] = (byte)(x * 7);
				pixels[offset + 1] = (byte)(y * 11);
				pixels[offset + 2] = (byte)((x + y) * 3);
				pixels[offset + 3] = 0xFF;
			}
		}

		return pixels;
	}
}
