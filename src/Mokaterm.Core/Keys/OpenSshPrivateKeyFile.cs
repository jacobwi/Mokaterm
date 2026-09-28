using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Mokaterm.Core.Security;

namespace Mokaterm.Core.Keys;

/// <summary>
/// The <c>openssh-key-v1</c> container, the format <c>ssh-keygen</c> has written since OpenSSH 7.8. Writing it is
/// what makes a generated key usable everywhere; reading it back gets at the public half, which the container
/// stores in the clear even for a passphrase protected key.
/// </summary>
internal static class OpenSshPrivateKeyFile
{
	public const string BeginMarker = "-----BEGIN OPENSSH PRIVATE KEY-----";

	public const string EndMarker = "-----END OPENSSH PRIVATE KEY-----";

	/// <summary>A private key file past this size is not one.</summary>
	public const int MaxTextLength = 512 * 1024;

	private const int Base64LineLength = 70;
	private const int SaltLength = 16;
	private const int AesKeyLength = 32;
	private const int AesBlockLength = 16;
	private const string CipherNone = "none";
	private const string CipherAes256Ctr = "aes256-ctr";
	private const string KdfNone = "none";
	private const string KdfBcrypt = "bcrypt";

	private static ReadOnlySpan<byte> Magic => "openssh-key-v1\0"u8;

	/// <summary>
	/// Writes one key pair. An empty <paramref name="passphrase"/> leaves the private half unencrypted, which is
	/// what OpenSSH does when the user gives no passphrase.
	/// </summary>
	public static string Write(
		ReadOnlySpan<byte> publicBlob,
		ReadOnlySpan<byte> privateBlob,
		string comment,
		ReadOnlySpan<byte> passphrase,
		int rounds = BcryptPbkdf.DefaultRounds)
	{
		bool encrypted = !passphrase.IsEmpty;

		// Unencrypted sections still pad to 8, which is what OpenSSH writes.
		int blockSize = encrypted ? AesBlockLength : 8;

		using PinnedBytes section = BuildSection(privateBlob, comment, blockSize);
		byte[]? salt = null;
		if (encrypted)
		{
			salt = RandomNumberGenerator.GetBytes(SaltLength);
			using PinnedBytes keyAndIv = new(AesKeyLength + AesBlockLength);
			BcryptPbkdf.DeriveKey(passphrase, salt, rounds, keyAndIv.Span);
			AesCtr.Transform(keyAndIv.Span[..AesKeyLength], keyAndIv.Span[AesKeyLength..], section.Span);
		}

		using PinnedBufferWriter container = new(4096);
		SshWire.WriteBytes(container, Magic);
		SshWire.WriteString(container, encrypted ? CipherAes256Ctr : CipherNone);
		SshWire.WriteString(container, encrypted ? KdfBcrypt : KdfNone);
		if (salt is not null)
		{
			using PinnedBufferWriter options = new(64);
			SshWire.WriteString(options, salt);
			SshWire.WriteUInt32(options, (uint)rounds);
			SshWire.WriteString(container, options.WrittenSpan);
		}
		else
		{
			SshWire.WriteUInt32(container, 0);
		}

		SshWire.WriteUInt32(container, 1);
		SshWire.WriteString(container, publicBlob);
		SshWire.WriteString(container, section.Span);
		return Armor(container.WrittenSpan);
	}

	/// <summary>
	/// Pulls the public key blob out of a private key file. The comment only comes back for an unencrypted key,
	/// because it sits inside the encrypted section.
	/// </summary>
	public static bool TryReadPublicPart(string text, out byte[] publicBlob, out string comment)
	{
		publicBlob = [];
		comment = "";
		if (!TryDecodeBody(text, out byte[]? blob))
		{
			return false;
		}

		if (blob.Length < Magic.Length || !blob.AsSpan(0, Magic.Length).SequenceEqual(Magic))
		{
			return false;
		}

		SshWireReader reader = new(blob.AsSpan(Magic.Length));
		if (!reader.TryReadString(out ReadOnlySpan<byte> cipher)
			|| !reader.TryReadString(out _)
			|| !reader.TryReadString(out _)
			|| !reader.TryReadUInt32(out uint keyCount)
			|| keyCount < 1
			|| !reader.TryReadString(out ReadOnlySpan<byte> publicKey)
			|| publicKey.IsEmpty)
		{
			return false;
		}

		publicBlob = publicKey.ToArray();
		if (Encoding.UTF8.GetString(cipher) == CipherNone && reader.TryReadString(out ReadOnlySpan<byte> section))
		{
			comment = ReadComment(section);
		}

		return true;
	}

	/// <summary>Wraps the base64 body in the PEM markers, in lines of 70 like OpenSSH writes them.</summary>
	private static string Armor(ReadOnlySpan<byte> blob)
	{
		string body = Convert.ToBase64String(blob);
		StringBuilder builder = new(body.Length + (body.Length / Base64LineLength) + 128);
		builder.Append(BeginMarker).Append('\n');
		for (int offset = 0; offset < body.Length; offset += Base64LineLength)
		{
			int length = Math.Min(Base64LineLength, body.Length - offset);
			builder.Append(body, offset, length).Append('\n');
		}

		return builder.Append(EndMarker).Append('\n').ToString();
	}

	private static PinnedBytes BuildSection(ReadOnlySpan<byte> privateBlob, string comment, int blockSize)
	{
		using PinnedBufferWriter writer = new(2048);

		// Two equal check words are how a reader knows the passphrase was right.
		uint check = BinaryPrimitives.ReadUInt32LittleEndian(RandomNumberGenerator.GetBytes(4));
		SshWire.WriteUInt32(writer, check);
		SshWire.WriteUInt32(writer, check);
		SshWire.WriteBytes(writer, privateBlob);
		SshWire.WriteString(writer, comment);

		int padding = writer.WrittenSpan.Length % blockSize;
		if (padding != 0)
		{
			Span<byte> tail = writer.GetSpan(blockSize);
			for (int i = 0; i < blockSize - padding; i++)
			{
				tail[i] = (byte)(i + 1);
			}

			writer.Advance(blockSize - padding);
		}

		return PinnedBytes.Copy(writer.WrittenSpan);
	}

	/// <summary>
	/// The comment is the last length prefixed field before the padding, and every field in the section is length
	/// prefixed, so walking them needs no knowledge of the key type.
	/// </summary>
	private static string ReadComment(ReadOnlySpan<byte> section)
	{
		SshWireReader reader = new(section);
		if (!reader.TrySkip(8))
		{
			return "";
		}

		ReadOnlySpan<byte> last = default;
		while (reader.TryReadString(out ReadOnlySpan<byte> field))
		{
			last = field;
		}

		return last.IsEmpty ? "" : Encoding.UTF8.GetString(last);
	}

	private static bool TryDecodeBody(string text, out byte[] blob)
	{
		blob = [];
		if (string.IsNullOrEmpty(text) || text.Length > MaxTextLength)
		{
			return false;
		}

		int start = text.IndexOf(BeginMarker, StringComparison.Ordinal);
		if (start < 0)
		{
			return false;
		}

		start += BeginMarker.Length;
		int end = text.IndexOf(EndMarker, start, StringComparison.Ordinal);
		if (end < 0)
		{
			return false;
		}

		// Convert.FromBase64String skips the line breaks itself.
		try
		{
			blob = Convert.FromBase64String(text[start..end]);
			return true;
		}
		catch (FormatException)
		{
			return false;
		}
	}
}
