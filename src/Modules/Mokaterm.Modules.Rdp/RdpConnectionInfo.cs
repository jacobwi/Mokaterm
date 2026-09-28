namespace Mokaterm.Modules.Rdp;

/// <summary>What the server told us about itself while connecting.</summary>
public sealed record RdpConnectionInfo
{
	/// <summary>Desktop width in pixels as negotiated, updated when the server resizes.</summary>
	public required int Width { get; init; }

	public required int Height { get; init; }

	/// <summary>True when the session runs inside TLS, which is every session this module opens.</summary>
	public required bool IsEncrypted { get; init; }

	/// <summary>How the session was secured, for example <c>TLS with network level authentication</c>.</summary>
	public required string Security { get; init; }

	/// <summary>
	/// True when the server's certificate was matched against the known hosts or accepted by the user. False means
	/// the connection is encrypted but nothing proves who is at the other end.
	/// </summary>
	public required bool IdentityVerified { get; init; }

	/// <summary>The certificate subject, when one was seen.</summary>
	public string? CertificateSubject { get; init; }

	/// <summary>SHA-256 of the certificate as uppercase hex, when one was seen.</summary>
	public string? CertificateFingerprint { get; init; }
}
