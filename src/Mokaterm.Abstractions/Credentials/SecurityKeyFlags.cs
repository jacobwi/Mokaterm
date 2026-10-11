namespace Mokaterm.Abstractions.Credentials;

/// <summary>
/// The flags byte of an OpenSSH security key file (<c>sk-api.h</c>). They say what the key was enrolled with and what
/// a signature has to ask the authenticator for.
/// </summary>
[Flags]
public enum SecurityKeyFlags : byte
{
	None = 0,

	/// <summary>Every signature needs a touch. Set unless the key was made with <c>no-touch-required</c>.</summary>
	UserPresenceRequired = 0x01,

	/// <summary>Every signature needs the key's PIN or a fingerprint (<c>verify-required</c>).</summary>
	UserVerificationRequired = 0x04,

	/// <summary>Enrolment may replace a resident credential that is already on the key.</summary>
	ForceOperation = 0x10,

	/// <summary>The credential lives on the key and can be loaded from it again (<c>ssh-keygen -K</c>).</summary>
	ResidentKey = 0x20,
}
