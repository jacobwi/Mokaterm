using System.Formats.Asn1;
using System.Text;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Security;
using Mokaterm.Modules.Ssh.Agent;

namespace Mokaterm.Modules.Ssh.SecurityKeys;

/// <summary>
/// Turns what an authenticator returned into the signature blob of an SSH publickey request, laid out as PROTOCOL.u2f
/// and <c>ssh-sk.c</c> write it: the signature type, the signature, the flags byte and the counter, with the origin,
/// client data and extensions after them for the WebAuthn type. What the authenticator signed is not repeated: the
/// server rebuilds it from the key's application, the flags, the counter and the message.
/// </summary>
internal static class SecurityKeySignature
{
	private const int Ed25519SignatureLength = 64;

	/// <summary>
	/// The plain <c>sk-</c> signature, for an assertion whose client data was the message itself, so that the
	/// authenticator's <c>SHA-256(client data)</c> is the message hash OpenSSH puts after the counter.
	/// </summary>
	/// <exception cref="SecurityKeyException">The assertion cannot be expressed as an OpenSSH signature.</exception>
	public static byte[] Encode(SshSecurityKey key, SecurityKeyAssertion assertion)
	{
		ArgumentNullException.ThrowIfNull(key);
		ArgumentNullException.ThrowIfNull(assertion);
		AuthenticatorDataHeader header = ReadHeader(key, assertion.AuthenticatorData);

		// OpenSSH signs no extensions into this type, so a server would rebuild different data and refuse it.
		if (assertion.AuthenticatorData.Length != AuthenticatorDataHeader.Length)
		{
			throw new SecurityKeyException(SecurityKeyFailure.NotSupported, "The security key added extension data to its signature, which an OpenSSH signature cannot carry.");
		}

		return new SshWireWriter()
			.WriteString(Encoding.ASCII.GetBytes(key.KeyType))
			.WriteString(InnerSignature(key.Algorithm, assertion.Signature))
			.WriteByte(header.Flags)
			.WriteUInt32(header.Counter)
			.ToArray();
	}

	/// <summary>
	/// The <c>webauthn-sk-ecdsa-sha2-nistp256@openssh.com</c> signature, for an assertion whose client data was the JSON
	/// <see cref="WebAuthnClientData.Build"/> made. Extensions travel with it, since sshd signs them back in.
	/// </summary>
	/// <exception cref="SecurityKeyException">The assertion cannot be expressed as an OpenSSH signature.</exception>
	public static byte[] EncodeWebAuthn(SshSecurityKey key, SecurityKeyAssertion assertion, string origin, ReadOnlySpan<byte> clientData)
	{
		ArgumentNullException.ThrowIfNull(key);
		ArgumentNullException.ThrowIfNull(assertion);
		if (key.Algorithm != SecurityKeyAlgorithm.EcdsaP256)
		{
			throw new ArgumentException("Only ECDSA security keys have a WebAuthn signature type.", nameof(key));
		}

		AuthenticatorDataHeader header = ReadHeader(key, assertion.AuthenticatorData);
		ReadOnlySpan<byte> extensions = assertion.AuthenticatorData.AsSpan(AuthenticatorDataHeader.Length);
		bool extensionFlag = (header.Flags & AuthenticatorDataHeader.ExtensionData) != 0;
		if ((header.Flags & AuthenticatorDataHeader.AttestedCredentialData) != 0 || extensionFlag != !extensions.IsEmpty)
		{
			throw new SecurityKeyException(SecurityKeyFailure.Failed, "The security key returned authenticator data that sshd would refuse.");
		}

		return new SshWireWriter()
			.WriteString(Encoding.ASCII.GetBytes(SecurityKeyTypes.WebAuthnEcdsaSignatureType))
			.WriteString(InnerSignature(key.Algorithm, assertion.Signature))
			.WriteByte(header.Flags)
			.WriteUInt32(header.Counter)
			.WriteString(Encoding.UTF8.GetBytes(origin))
			.WriteString(clientData)
			.WriteString(extensions)
			.ToArray();
	}

	private static AuthenticatorDataHeader ReadHeader(SshSecurityKey key, byte[] authenticatorData) =>
		AuthenticatorDataHeader.TryRead(authenticatorData, key.Application, out AuthenticatorDataHeader header)
			? header
			: throw new SecurityKeyException(SecurityKeyFailure.Failed, "The security key signed for a different application than the one this key belongs to.");

	/// <summary>
	/// Ed25519 goes as the 64 bytes it is. ECDSA arrives as a DER sequence of r and s, and goes as two mpints: a DER
	/// integer's content is already the minimal two's complement an mpint is, so the bytes move across unchanged.
	/// </summary>
	private static byte[] InnerSignature(SecurityKeyAlgorithm algorithm, byte[] signature)
	{
		if (algorithm == SecurityKeyAlgorithm.Ed25519)
		{
			return signature.Length == Ed25519SignatureLength
				? signature
				: throw Unreadable();
		}

		try
		{
			AsnReader reader = new(signature, AsnEncodingRules.DER);
			AsnReader sequence = reader.ReadSequence();
			ReadOnlyMemory<byte> r = sequence.ReadIntegerBytes();
			ReadOnlyMemory<byte> s = sequence.ReadIntegerBytes();
			sequence.ThrowIfNotEmpty();
			reader.ThrowIfNotEmpty();
			if (!IsPositive(r.Span) || !IsPositive(s.Span))
			{
				throw Unreadable();
			}

			return new SshWireWriter().WriteString(r.Span).WriteString(s.Span).ToArray();
		}
		catch (AsnContentException ex)
		{
			throw new SecurityKeyException(SecurityKeyFailure.Failed, "The security key returned a signature that could not be read.", ex);
		}
	}

	private static bool IsPositive(ReadOnlySpan<byte> integer) =>
		!integer.IsEmpty && (integer[0] & 0x80) == 0 && !(integer.Length == 1 && integer[0] == 0);

	private static SecurityKeyException Unreadable() =>
		new(SecurityKeyFailure.Failed, "The security key returned a signature that could not be read.");
}
