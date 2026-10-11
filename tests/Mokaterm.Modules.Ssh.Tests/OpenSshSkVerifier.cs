using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Mokaterm.Modules.Ssh.Agent;
using BouncyEd25519 = Org.BouncyCastle.Math.EC.Rfc8032.Ed25519;

namespace Mokaterm.Modules.Ssh.Tests;

/// <summary>
/// OpenSSH's checks for a security key signature, ported line for line from <c>ssh_ecdsa_sk_verify</c>,
/// <c>webauthn_check_prepare_hash</c> (ssh-ecdsa-sk.c) and <c>ssh_ed25519_sk_verify</c> (ssh-ed25519-sk.c): what sshd
/// runs on the signature in a publickey request. It verifies OpenSSH's own test signatures first, so a signature it
/// accepts is one sshd accepts.
/// </summary>
internal static class OpenSshSkVerifier
{
	private const string EcdsaKeyType = "sk-ecdsa-sha2-nistp256@openssh.com";
	private const string Ed25519KeyType = "sk-ssh-ed25519@openssh.com";
	private const string WebAuthnSignatureType = "webauthn-sk-ecdsa-sha2-nistp256@openssh.com";
	private const int MaxBignumBytes = 33;

	/// <summary>True when sshd would accept <paramref name="signature"/> by the key in <paramref name="publicKeyBlob"/> over <paramref name="data"/>.</summary>
	public static bool Verify(byte[] publicKeyBlob, byte[] signature, byte[] data)
	{
		SshWireReader key = new(publicKeyBlob);
		if (!key.TryReadText(out string keyType))
		{
			return false;
		}

		return keyType switch
		{
			EcdsaKeyType => VerifyEcdsa(key, signature, data),
			Ed25519KeyType => VerifyEd25519(key, signature, data),
			_ => false,
		};
	}

	private static bool VerifyEcdsa(SshWireReader key, byte[] signature, byte[] data)
	{
		if (!key.TryReadText(out string curve) || curve != "nistp256"
			|| !key.TryReadString(out byte[] point) || point.Length != 65 || point[0] != 0x04
			|| !key.TryReadText(out string application) || key.Remaining != 0)
		{
			return false;
		}

		SshWireReader b = new(signature);
		if (!b.TryReadText(out string signatureType))
		{
			return false;
		}

		bool webAuthn = signatureType == WebAuthnSignatureType;
		if (!webAuthn && signatureType != EcdsaKeyType)
		{
			return false;
		}

		if (!b.TryReadString(out byte[] inner) || !b.TryReadByte(out byte flags) || !b.TryReadUInt32(out uint counter))
		{
			return false;
		}

		string origin = "";
		byte[] wrapper = [];
		byte[] extensions = [];
		if (webAuthn && (!b.TryReadText(out origin) || origin.Contains('\0', StringComparison.Ordinal) || !b.TryReadString(out wrapper) || !b.TryReadString(out extensions)))
		{
			return false;
		}

		if (b.Remaining != 0)
		{
			return false;
		}

		SshWireReader innerReader = new(inner);
		if (!TryReadBignum(innerReader, out byte[] r) || !TryReadBignum(innerReader, out byte[] s) || innerReader.Remaining != 0)
		{
			return false;
		}

		byte[] messageHash;
		if (webAuthn)
		{
			if (!WebAuthnCheckPrepareHash(data, origin, wrapper, flags, extensions, out messageHash))
			{
				return false;
			}
		}
		else
		{
			messageHash = SHA256.HashData(data);
		}

		byte[] signed = Signed(application, flags, counter, extensions, messageHash);
		using ECDsa verifier = ECDsa.Create(new ECParameters
		{
			Curve = ECCurve.NamedCurves.nistP256,
			Q = new ECPoint { X = point[1..33], Y = point[33..] },
		});

		// EVP_DigestVerify with SHA-256 over the rebuilt data: r and s as a fixed width pair is the same check.
		return r.Length <= 32 && s.Length <= 32 && verifier.VerifyData(signed, (byte[])[.. Pad(r), .. Pad(s)], HashAlgorithmName.SHA256);
	}

	private static bool VerifyEd25519(SshWireReader key, byte[] signature, byte[] data)
	{
		if (!key.TryReadString(out byte[] publicKey) || publicKey.Length != 32 || !key.TryReadText(out string application) || key.Remaining != 0)
		{
			return false;
		}

		SshWireReader b = new(signature);
		if (!b.TryReadText(out string signatureType)
			|| !b.TryReadString(out byte[] signatureBlob)
			|| !b.TryReadByte(out byte flags)
			|| !b.TryReadUInt32(out uint counter)
			|| signatureType != Ed25519KeyType
			|| b.Remaining != 0
			|| signatureBlob.Length != 64)
		{
			return false;
		}

		byte[] signed = Signed(application, flags, counter, [], SHA256.HashData(data));
		return BouncyEd25519.Verify(signatureBlob, 0, publicKey, 0, signed, 0, signed.Length);
	}

	/// <summary>apphash, flags, counter, extensions (WebAuthn only), message hash: what the authenticator signed.</summary>
	private static byte[] Signed(string application, byte flags, uint counter, byte[] extensions, byte[] messageHash) =>
	[
		.. SHA256.HashData(Encoding.UTF8.GetBytes(application)),
		flags,
		(byte)(counter >> 24),
		(byte)(counter >> 16),
		(byte)(counter >> 8),
		(byte)counter,
		.. extensions,
		.. messageHash,
	];

	private static bool WebAuthnCheckPrepareHash(byte[] data, string origin, byte[] wrapper, byte flags, byte[] extensions, out byte[] messageHash)
	{
		messageHash = [];
		if (origin.Contains('"', StringComparison.Ordinal)
			|| (flags & 0x40) != 0
			|| ((flags & 0x80) == 0) != (extensions.Length == 0))
		{
			return false;
		}

		byte[] preamble = Encoding.UTF8.GetBytes(
			"{\"type\":\"webauthn.get\",\"challenge\":\"" + Base64Url.EncodeToString(data) + "\",\"origin\":\"" + origin + "\"");
		if (!wrapper.AsSpan().StartsWith(preamble))
		{
			return false;
		}

		messageHash = SHA256.HashData(wrapper);
		return true;
	}

	/// <summary><c>sshbuf_get_bignum2_bytes_direct</c>: no sign bit, no more than one byte of zero prefix, zeros trimmed.</summary>
	private static bool TryReadBignum(SshWireReader reader, out byte[] value)
	{
		if (!reader.TryReadString(out value) || value.Length > MaxBignumBytes || (value.Length > 0 && (value[0] & 0x80) != 0))
		{
			return false;
		}

		int start = 0;
		while (start < value.Length && value[start] == 0)
		{
			start++;
		}

		value = value[start..];
		return true;
	}

	private static byte[] Pad(byte[] value) => [.. new byte[32 - value.Length], .. value];
}
