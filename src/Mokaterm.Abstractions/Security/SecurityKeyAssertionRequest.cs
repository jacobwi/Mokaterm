using System.Text;

namespace Mokaterm.Abstractions.Security;

/// <summary>One signature from one credential.</summary>
public sealed record SecurityKeyAssertionRequest
{
	/// <summary>The relying party id, which for an SSH key is its application string such as <c>ssh:</c>.</summary>
	public required string RelyingPartyId { get; init; }

	/// <summary>
	/// What the platform hashes as the client data. WebAuthn documents it as JSON, but the platform only hashes it, so an
	/// SSH login hands over the message itself and gets the plain OpenSSH signature back.
	/// </summary>
	public required byte[] ClientData { get; init; }

	/// <summary>The credential id, which an SSH key file calls its key handle.</summary>
	public required byte[] CredentialId { get; init; }

	/// <summary>Ask for the key's PIN or a fingerprint, for a key made with <c>verify-required</c>.</summary>
	public bool RequireUserVerification { get; init; }

	/// <summary>How long the prompt may wait for the user.</summary>
	public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(2);

	private bool PrintMembers(StringBuilder builder)
	{
		builder
			.Append("RelyingPartyId = ").Append(RelyingPartyId)
			.Append(", ClientData = ").Append(ClientData.Length).Append(" bytes")
			.Append(", CredentialId = ").Append(CredentialId.Length).Append(" bytes")
			.Append(", RequireUserVerification = ").Append(RequireUserVerification)
			.Append(", Timeout = ").Append(Timeout);
		return true;
	}
}
