using System.Text;
using Mokaterm.Abstractions.Credentials;

namespace Mokaterm.Abstractions.Security;

/// <summary>A new credential, with the choices <c>ssh-keygen -t ecdsa-sk</c> or <c>ed25519-sk</c> offers.</summary>
public sealed record SecurityKeyCredentialRequest
{
	/// <summary>The relying party id, which becomes the key's application string.</summary>
	public required string RelyingPartyId { get; init; }

	public required SecurityKeyAlgorithm Algorithm { get; init; }

	/// <summary>The client data for the attestation. Its signature is not kept, so random bytes do.</summary>
	public required byte[] Challenge { get; init; }

	/// <summary>The user handle. OpenSSH uses 32 zero bytes, which <c>ssh-keygen -K</c> expects to find.</summary>
	public required byte[] UserId { get; init; }

	public required string UserName { get; init; }

	/// <summary>Keep the credential on the device, so it can be loaded again without the key file.</summary>
	public bool ResidentKey { get; init; }

	/// <summary>Ask for the PIN or a fingerprint now, and mark every signature as needing it.</summary>
	public bool RequireUserVerification { get; init; }

	public SecurityKeyProtection Protection { get; init; }

	public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(2);

	private bool PrintMembers(StringBuilder builder)
	{
		builder
			.Append("RelyingPartyId = ").Append(RelyingPartyId)
			.Append(", Algorithm = ").Append(Algorithm)
			.Append(", Challenge = ").Append(Challenge.Length).Append(" bytes")
			.Append(", UserId = ").Append(UserId.Length).Append(" bytes")
			.Append(", UserName = ").Append(UserName)
			.Append(", ResidentKey = ").Append(ResidentKey)
			.Append(", RequireUserVerification = ").Append(RequireUserVerification)
			.Append(", Protection = ").Append(Protection)
			.Append(", Timeout = ").Append(Timeout);
		return true;
	}
}
