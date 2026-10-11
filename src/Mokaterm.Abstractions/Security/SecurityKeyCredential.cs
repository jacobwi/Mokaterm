namespace Mokaterm.Abstractions.Security;

/// <summary>A credential an authenticator just made.</summary>
/// <param name="AuthenticatorData">Raw authenticator data with the attested credential data: the public key is in there.</param>
/// <param name="CredentialId">The credential id the platform reported, which must match the one in the data.</param>
public sealed record SecurityKeyCredential(byte[] AuthenticatorData, byte[] CredentialId);
