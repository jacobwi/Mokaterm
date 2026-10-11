using System.Buffers.Text;
using System.Text;
using Mokaterm.Abstractions.Credentials;

namespace Mokaterm.Modules.Ssh.SecurityKeys;

/// <summary>
/// The client data JSON of a <c>webauthn-sk-ecdsa-sha2-nistp256@openssh.com</c> signature. sshd parses no JSON: it
/// rebuilds the start of the text from the message and the origin and compares bytes (<c>webauthn_check_prepare_hash</c>
/// in ssh-ecdsa-sk.c), so the type, the challenge and the origin have to come first, in that order, unescaped.
/// </summary>
internal static class WebAuthnClientData
{
	/// <summary>
	/// True when the key can sign this way at all: ECDSA only, since OpenSSH defines no WebAuthn signature for Ed25519,
	/// and an origin that JSON can hold without escaping, since sshd compares it raw.
	/// </summary>
	public static bool CanCarry(SshSecurityKey key)
	{
		ArgumentNullException.ThrowIfNull(key);
		return key.Algorithm == SecurityKeyAlgorithm.EcdsaP256 && IsPlainOrigin(key.Application);
	}

	/// <summary>The challenge is the whole message, base64url without padding, which is how sshd encodes it to compare.</summary>
	public static byte[] Build(ReadOnlySpan<byte> message, string origin)
	{
		if (!IsPlainOrigin(origin))
		{
			throw new ArgumentException("The origin cannot be written into the client data unescaped.", nameof(origin));
		}

		string json = "{\"type\":\"webauthn.get\",\"challenge\":\""
			+ Base64Url.EncodeToString(message)
			+ "\",\"origin\":\""
			+ origin
			+ "\",\"crossOrigin\":false}";
		return Encoding.UTF8.GetBytes(json);
	}

	private static bool IsPlainOrigin(string origin) =>
		origin.Length > 0 && origin.All(character => character is >= ' ' and <= '~' and not '"' and not '\\');
}
