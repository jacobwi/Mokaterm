namespace Mokaterm.Abstractions.Security;

/// <summary>The CTAP2 <c>credProtect</c> level a new credential asks for. The values are the protocol's own.</summary>
public enum SecurityKeyProtection
{
	/// <summary>No extension is sent; the authenticator's default applies.</summary>
	None = 0,

	/// <summary>
	/// The credential is only found by someone who already has its id, or after the PIN. OpenSSH asks for this on a
	/// resident key, so the key cannot be listed off a lost device without its PIN.
	/// </summary>
	UserVerificationOptionalWithCredentialIdList = 2,

	/// <summary>Every use needs the PIN or a fingerprint.</summary>
	UserVerificationRequired = 3,
}
