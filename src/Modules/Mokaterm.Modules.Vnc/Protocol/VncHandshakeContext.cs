using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Security;

namespace Mokaterm.Modules.Vnc.Protocol;

/// <summary>Everything the RFB handshake needs for one attempt.</summary>
internal sealed record VncHandshakeContext
{
	public required string Host { get; init; }

	public required int Port { get; init; }

	public required IHostIdentityVerifier Verifier { get; init; }

	public VncEncryptionMode Encryption { get; init; } = VncEncryptionMode.Preferred;

	/// <summary>The ClientInit flag that lets other viewers keep their connection.</summary>
	public bool Shared { get; init; } = true;

	/// <summary>Login material for the security types that need it. Null means no password is available.</summary>
	public LoginCredentials? Credentials { get; init; }

	public IProgress<string>? Status { get; init; }

	public string Endpoint => HostEndpoint.Format(Host, Port);
}
