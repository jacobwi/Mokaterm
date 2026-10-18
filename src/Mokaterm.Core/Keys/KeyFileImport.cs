using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Security;
using Mokaterm.Core.Security;

namespace Mokaterm.Core.Keys;

/// <summary>
/// Turns the bytes of a key file a login names into what the keychain stores, or into the reason it cannot. An OpenSSH
/// file, security key files among them, is kept as it is. A PuTTY or RSA PEM file without a passphrase is checked and
/// converted to OpenSSH. A protected PuTTY or PEM file is kept as it is too, because opening it needs the passphrase,
/// which the login asks for when it connects.
/// </summary>
internal static class KeyFileImport
{
	/// <summary>A key file past this is not one; the largest RSA key PuTTYgen makes is under 13 KB.</summary>
	public const int MaxFileBytes = PuttyKeyFile.MaxTextLength;

	private static ReadOnlySpan<byte> Whitespace => " \t\r\n"u8;

	private static ReadOnlySpan<byte> ByteOrderMark => [0xEF, 0xBB, 0xBF];

	private static ReadOnlySpan<byte> RsaPemBegin => "-----BEGIN RSA PRIVATE KEY-----"u8;

	private static ReadOnlySpan<byte> PemEncrypted => "Proc-Type: 4,ENCRYPTED"u8;

	private static ReadOnlySpan<byte> PemBegin => "-----BEGIN "u8;

	private static ReadOnlySpan<byte> PrivateKeyLabelEnd => "PRIVATE KEY-----"u8;

	public static KeyFileMaterial Read(ReadOnlySpan<byte> bytes)
	{
		// Notepad leaves a byte order mark on a file it saved, and nothing that reads keys expects one.
		ReadOnlySpan<byte> text = bytes.Trim(Whitespace);
		text = text.StartsWith(ByteOrderMark) ? text[ByteOrderMark.Length..].TrimStart(Whitespace) : text;
		if (text.IsEmpty)
		{
			return KeyFileMaterial.Unreadable("The key file is empty.");
		}

		if (text.Length > MaxFileBytes)
		{
			return KeyFileMaterial.Unreadable("The file is too large to be a private key.");
		}

		if (PuttyKeyFile.LooksLikeOne(text))
		{
			return ReadPutty(text);
		}

		if (text.IndexOf(Encoding.ASCII.GetBytes(OpenSshPrivateKeyFile.BeginMarker)) >= 0)
		{
			return ReadOpenSsh(text);
		}

		if (text.IndexOf(RsaPemBegin) >= 0)
		{
			return ReadRsaPem(text);
		}

		if (text.IndexOf(PemBegin) >= 0 && text.IndexOf(PrivateKeyLabelEnd) >= 0)
		{
			return KeyFileMaterial.Unsupported(
				"Only OpenSSH, PuTTY and RSA PEM keys are imported. Paste this one into the keychain by hand instead.");
		}

		return IsPublicKey(text)
			? KeyFileMaterial.Unsupported("This file holds a public key. The login has to name the private key, usually the same name without .pub.")
			: KeyFileMaterial.Unreadable("This file is not a private key.");
	}

	/// <summary>True for the key types the SSH module signs with, which are the ones worth putting on a login.</summary>
	public static bool IsSupportedAlgorithm(string algorithm) =>
		PuttyKeyFile.IsSupportedAlgorithm(algorithm) || SecurityKeyTypes.IsSecurityKey(algorithm);

	private static KeyFileMaterial ReadOpenSsh(ReadOnlySpan<byte> text)
	{
		if (!OpenSshPrivateKeyFile.TryReadHeader(text, out byte[] publicBlob, out bool encrypted))
		{
			return KeyFileMaterial.Unreadable("The OpenSSH key file is damaged or incomplete, or holds more than one key.");
		}

		string algorithm = AlgorithmOf(publicBlob);
		if (!IsSupportedAlgorithm(algorithm))
		{
			return KeyFileMaterial.Unsupported(PuttyKeyFile.UnsupportedAlgorithm(algorithm));
		}

		if (!encrypted)
		{
			// The check words show whether the private section came through whole.
			PrivateKeyStatus status = OpenSshPrivateKeyFile.TryOpen(text, null, out _, out PinnedBytes? section, out string? error);
			section?.Dispose();
			if (status != PrivateKeyStatus.Valid)
			{
				return Failed(status, error);
			}
		}

		return KeyFileMaterial.Usable(SecretBuffer.FromBytes(text), algorithm, Fingerprint(publicBlob), encrypted);
	}

	private static KeyFileMaterial ReadPutty(ReadOnlySpan<byte> text)
	{
		PrivateKeyStatus status = PuttyKeyFile.TryReadPublicPart(text, out PuttyPublicPart? part, out string? error);
		if (part is null)
		{
			return Failed(status, error);
		}

		if (!PuttyKeyFile.IsSupportedAlgorithm(part.Algorithm))
		{
			return KeyFileMaterial.Unsupported(PuttyKeyFile.UnsupportedAlgorithm(part.Algorithm));
		}

		if (part.IsEncrypted)
		{
			// Kept as PuTTY wrote it: without the passphrase there is no MAC to check, and the fingerprint is the file's word.
			return KeyFileMaterial.Usable(SecretBuffer.FromBytes(text), part.Algorithm, Fingerprint(part.PublicBlob), needsPassphrase: true);
		}

		status = PuttyKeyFile.TryConvert(text, null, out string? openSshKey, out byte[] publicBlob, out error);
		return openSshKey is null
			? Failed(status, error)
			: KeyFileMaterial.Usable(SecretBuffer.FromString(openSshKey), part.Algorithm, Fingerprint(publicBlob), needsPassphrase: false);
	}

	private static KeyFileMaterial ReadRsaPem(ReadOnlySpan<byte> text)
	{
		if (text.IndexOf(PemEncrypted) >= 0)
		{
			// The public half sits inside the encrypted part, so there is no fingerprint until the passphrase opens it.
			return KeyFileMaterial.Usable(SecretBuffer.FromBytes(text), SshKeyBlobs.Rsa, fingerprint: null, needsPassphrase: true);
		}

		char[] characters = GC.AllocateArray<char>(text.Length, pinned: true);
		try
		{
			int count = Encoding.ASCII.GetChars(text, characters);
			using RSA rsa = RSA.Create();
			rsa.ImportFromPem(characters.AsSpan(0, count));
			RSAParameters parameters = rsa.ExportParameters(includePrivateParameters: true);
			try
			{
				using PinnedBufferWriter publicWriter = new(1024);
				SshKeyBlobs.WriteRsaPublic(publicWriter, Field(parameters.Exponent), Field(parameters.Modulus));
				using PinnedBufferWriter privateWriter = new(4096);
				SshKeyBlobs.WriteRsaPrivate(
					privateWriter,
					Field(parameters.Modulus),
					Field(parameters.Exponent),
					Field(parameters.D),
					Field(parameters.InverseQ),
					Field(parameters.P),
					Field(parameters.Q));

				string openSshKey = OpenSshPrivateKeyFile.Write(publicWriter.WrittenSpan, privateWriter.WrittenSpan, "", passphrase: default);
				return KeyFileMaterial.Usable(SecretBuffer.FromString(openSshKey), SshKeyBlobs.Rsa, Fingerprint(publicWriter.WrittenSpan), needsPassphrase: false);
			}
			finally
			{
				SshKeyGenerator.Wipe(parameters.D);
				SshKeyGenerator.Wipe(parameters.P);
				SshKeyGenerator.Wipe(parameters.Q);
				SshKeyGenerator.Wipe(parameters.DP);
				SshKeyGenerator.Wipe(parameters.DQ);
				SshKeyGenerator.Wipe(parameters.InverseQ);
			}
		}
		catch (Exception ex) when (ex is CryptographicException or ArgumentException)
		{
			return KeyFileMaterial.Unreadable("The RSA key is damaged or incomplete.");
		}
		finally
		{
			CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(characters.AsSpan()));
		}
	}

	private static byte[] Field(byte[]? value) =>
		value ?? throw new CryptographicException("The key is missing a parameter.");

	private static KeyFileMaterial Failed(PrivateKeyStatus status, string? error)
	{
		string reason = string.IsNullOrWhiteSpace(error) ? "The key file is damaged or incomplete." : error;
		return status == PrivateKeyStatus.Unsupported ? KeyFileMaterial.Unsupported(reason) : KeyFileMaterial.Unreadable(reason);
	}

	private static string AlgorithmOf(ReadOnlySpan<byte> publicBlob)
	{
		SshWireReader reader = new(publicBlob);
		return reader.TryReadString(out ReadOnlySpan<byte> name) ? Encoding.ASCII.GetString(name) : "";
	}

	private static string Fingerprint(ReadOnlySpan<byte> publicBlob) => SshKeyGenerator.Describe(publicBlob, "").Fingerprint;

	private static bool IsPublicKey(ReadOnlySpan<byte> text) =>
		text.StartsWith("ssh-"u8)
		|| text.StartsWith("ecdsa-"u8)
		|| text.StartsWith("sk-"u8)
		|| text.StartsWith("---- BEGIN SSH2 PUBLIC KEY ----"u8);
}
