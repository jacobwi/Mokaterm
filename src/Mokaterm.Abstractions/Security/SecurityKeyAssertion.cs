namespace Mokaterm.Abstractions.Security;

/// <summary>
/// What an authenticator signed and the signature itself, as the platform returned them: DER for ECDSA, 64 raw bytes for
/// Ed25519.
/// </summary>
/// <param name="AuthenticatorData">The relying party id hash, the flags, the counter and any extensions.</param>
/// <param name="Signature">The signature over <paramref name="AuthenticatorData"/> followed by the client data hash.</param>
public sealed record SecurityKeyAssertion(byte[] AuthenticatorData, byte[] Signature);
