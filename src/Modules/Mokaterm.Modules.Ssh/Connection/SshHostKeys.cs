using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Mokaterm.Abstractions.Security;
using Renci.SshNet.Common;

namespace Mokaterm.Modules.Ssh.Connection;

/// <summary>SSH public key fingerprints and host identities in the formats OpenSSH shows.</summary>
internal static class SshHostKeys
{
	private const string FingerprintPrefix = "SHA256:";
	private const int MaxKeyTypeLength = 64;

	/// <summary><c>SHA256:</c> plus the unpadded base64 SHA-256 of the key blob, as <c>ssh-keygen -l</c> prints it.</summary>
	public static string Fingerprint(ReadOnlySpan<byte> publicKeyBlob)
	{
		Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
		SHA256.HashData(publicKeyBlob, hash);
		return FingerprintPrefix + Convert.ToBase64String(hash).TrimEnd('=');
	}

	/// <summary>The key type written at the start of an SSH public key blob, such as <c>ssh-ed25519</c>, or null.</summary>
	public static string? ReadKeyType(ReadOnlySpan<byte> publicKeyBlob)
	{
		if (publicKeyBlob.Length < 4)
		{
			return null;
		}

		uint length = BinaryPrimitives.ReadUInt32BigEndian(publicKeyBlob);
		if (length is 0 or > MaxKeyTypeLength || length > (uint)(publicKeyBlob.Length - 4))
		{
			return null;
		}

		ReadOnlySpan<byte> name = publicKeyBlob.Slice(4, (int)length);
		foreach (byte b in name)
		{
			if (b is < 0x21 or > 0x7E)
			{
				return null;
			}
		}

		return Encoding.ASCII.GetString(name);
	}

	/// <summary>
	/// The identity to verify. The algorithm is the key type inside the blob (<c>ssh-rsa</c> whichever RSA signature was
	/// negotiated) so a known host keeps matching.
	/// </summary>
	/// <exception cref="CryptographicException">Our fingerprint disagrees with SSH.NET's for the same key.</exception>
	public static HostIdentity CreateIdentity(string host, int port, HostKeyEventArgs e)
	{
		ArgumentNullException.ThrowIfNull(e);
		byte[] blob = e.HostKey;
		string fingerprint = Fingerprint(blob);
		if (!string.Equals(fingerprint, FingerprintPrefix + e.FingerPrintSHA256, StringComparison.Ordinal))
		{
			throw new CryptographicException("The host key fingerprint could not be confirmed.");
		}

		return new HostIdentity
		{
			Host = host,
			Port = port,
			Kind = HostIdentityKind.SshHostKey,
			Algorithm = ReadKeyType(blob) ?? e.HostKeyName,
			Fingerprint = fingerprint,
		};
	}
}
