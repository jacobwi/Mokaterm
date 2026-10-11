using System.Buffers;
using System.Buffers.Binary;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Core.Security;

namespace Mokaterm.Core.Keys;

/// <summary>
/// The <c>openssh-key-v1</c> container, the format <c>ssh-keygen</c> has written since OpenSSH 7.8. Writing it is
/// what makes a generated key usable everywhere; reading it back gets at the public half, which the container
/// stores in the clear even for a passphrase protected key, and at the private section of the key types SSH.NET
/// cannot load, which are the security keys.
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
	private const int CheckWordsLength = 8;
	private const int UnencryptedBlockLength = 8;
	private const int MaxNameLength = 40;

	/// <summary>
	/// Key derivation rounds past this would keep a hostile file busy for minutes. OpenSSH writes 16 unless told
	/// otherwise, and even a generous <c>-a 100</c> stays far below.
	/// </summary>
	public const int MaxKdfRounds = 1024;

	private static readonly CipherSpec[] Ciphers =
	[
		new(CipherNone, 0, Cbc: false),
		new("aes128-ctr", 16, Cbc: false),
		new("aes192-ctr", 24, Cbc: false),
		new(CipherAes256Ctr, 32, Cbc: false),
		new("aes128-cbc", 16, Cbc: true),
		new("aes192-cbc", 24, Cbc: true),
		new("aes256-cbc", 32, Cbc: true),
	];

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
			comment = ReadComment(publicKey, section);
		}

		return true;
	}

	/// <summary>
	/// Opens a file holding one key and decrypts its private section, which comes back as it is stored: the two check
	/// words, the key's fields, the comment and the padding. The caller disposes <paramref name="section"/>.
	/// </summary>
	public static PrivateKeyStatus TryOpen(ReadOnlySpan<byte> text, string? passphrase, out byte[] publicBlob, out PinnedBytes? section, out string? error)
	{
		publicBlob = [];
		section = null;
		using PinnedBytes? body = DecodeBody(text);
		if (body is null || !body.Span.StartsWith(Magic))
		{
			error = "This is not a private key in OpenSSH format.";
			return PrivateKeyStatus.Invalid;
		}

		SshWireReader reader = new(body.Span[Magic.Length..]);
		if (!reader.TryReadString(out ReadOnlySpan<byte> cipherName)
			|| !reader.TryReadString(out ReadOnlySpan<byte> kdfName)
			|| !reader.TryReadString(out ReadOnlySpan<byte> kdfOptions)
			|| !reader.TryReadUInt32(out uint keyCount))
		{
			error = "The private key is damaged or incomplete.";
			return PrivateKeyStatus.Invalid;
		}

		if (keyCount != 1)
		{
			error = "This file holds more than one key, which ssh-keygen never writes.";
			return PrivateKeyStatus.Unsupported;
		}

		if (!reader.TryReadString(out ReadOnlySpan<byte> publicKey)
			|| publicKey.IsEmpty
			|| !reader.TryReadString(out ReadOnlySpan<byte> stored))
		{
			error = "The private key is damaged or incomplete.";
			return PrivateKeyStatus.Invalid;
		}

		string cipher = Encoding.UTF8.GetString(cipherName);
		CipherSpec? spec = Array.Find(Ciphers, candidate => candidate.Name == cipher);
		if (spec is null)
		{
			error = $"The key file is encrypted with {Printable(cipher, "a cipher")} that Mokaterm cannot read. ssh-keygen -p -Z aes256-ctr re-encrypts it with one it can.";
			return PrivateKeyStatus.Unsupported;
		}

		bool encrypted = spec.KeyLength > 0;
		PrivateKeyStatus status = encrypted
			? Decrypt(spec, kdfName, kdfOptions, stored, passphrase, out section, out error)
			: CopyPlain(kdfName, stored, out section, out error);
		if (section is null)
		{
			return status;
		}

		// Equal check words are how OpenSSH tells a right passphrase from a wrong one.
		ReadOnlySpan<byte> words = section.Span;
		if (words.Length < CheckWordsLength || BinaryPrimitives.ReadUInt32BigEndian(words) != BinaryPrimitives.ReadUInt32BigEndian(words[4..]))
		{
			section.Dispose();
			section = null;
			error = encrypted ? "The passphrase is wrong." : "The private key is damaged or incomplete.";
			return encrypted ? PrivateKeyStatus.WrongPassphrase : PrivateKeyStatus.Invalid;
		}

		publicBlob = publicKey.ToArray();
		error = null;
		return PrivateKeyStatus.Valid;
	}

	private static PrivateKeyStatus CopyPlain(ReadOnlySpan<byte> kdfName, ReadOnlySpan<byte> stored, out PinnedBytes? section, out string? error)
	{
		section = null;
		if (Encoding.UTF8.GetString(kdfName) != KdfNone || stored.Length % UnencryptedBlockLength != 0)
		{
			error = "The private key is damaged or incomplete.";
			return PrivateKeyStatus.Invalid;
		}

		section = PinnedBytes.Copy(stored);
		error = null;
		return PrivateKeyStatus.Valid;
	}

	private static PrivateKeyStatus Decrypt(
		CipherSpec spec,
		ReadOnlySpan<byte> kdfName,
		ReadOnlySpan<byte> kdfOptions,
		ReadOnlySpan<byte> stored,
		string? passphrase,
		out PinnedBytes? section,
		out string? error)
	{
		section = null;
		string kdf = Encoding.UTF8.GetString(kdfName);
		if (kdf != KdfBcrypt)
		{
			error = $"The key file is protected with {Printable(kdf, "a key derivation")} that Mokaterm cannot read.";
			return PrivateKeyStatus.Unsupported;
		}

		SshWireReader options = new(kdfOptions);
		if (!options.TryReadString(out ReadOnlySpan<byte> salt)
			|| salt.IsEmpty
			|| !options.TryReadUInt32(out uint rounds)
			|| rounds == 0
			|| stored.Length % AesBlockLength != 0)
		{
			error = "The private key is damaged or incomplete.";
			return PrivateKeyStatus.Invalid;
		}

		if (rounds > MaxKdfRounds)
		{
			error = $"The key file asks for more than {MaxKdfRounds} key derivation rounds, which would take minutes to open.";
			return PrivateKeyStatus.Unsupported;
		}

		if (string.IsNullOrEmpty(passphrase))
		{
			error = "The key is protected by a passphrase.";
			return PrivateKeyStatus.PassphraseRequired;
		}

		using PinnedBytes password = PinnedBytes.FromString(passphrase);
		using PinnedBytes keyAndIv = new(spec.KeyLength + AesBlockLength);
		BcryptPbkdf.DeriveKey(password.Span, salt, (int)rounds, keyAndIv.Span);
		section = PinnedBytes.Copy(stored);
		spec.Decrypt(keyAndIv.Span[..spec.KeyLength], keyAndIv.Span[spec.KeyLength..], section.Span);
		error = null;
		return PrivateKeyStatus.Valid;
	}

	/// <summary>A name read from the file, quoted only when it is short printable text, since the file may be hostile.</summary>
	private static string Printable(string name, string fallback) =>
		name.Length is > 0 and <= MaxNameLength && name.All(character => character is >= '!' and <= '~')
			? fallback + " (" + name + ")"
			: fallback;

	/// <summary>The base64 body between the markers, decoded into pinned memory, since an unencrypted one is secret.</summary>
	private static PinnedBytes? DecodeBody(ReadOnlySpan<byte> text)
	{
		if (text.IsEmpty || text.Length > MaxTextLength)
		{
			return null;
		}

		int start = text.IndexOf(Encoding.ASCII.GetBytes(BeginMarker));
		if (start < 0)
		{
			return null;
		}

		ReadOnlySpan<byte> rest = text[(start + BeginMarker.Length)..];
		int end = rest.IndexOf(Encoding.ASCII.GetBytes(EndMarker));
		if (end < 0)
		{
			return null;
		}

		ReadOnlySpan<byte> armored = rest[..end];
		using PinnedBytes compact = new(armored.Length);
		int count = 0;
		foreach (byte b in armored)
		{
			if (b is not ((byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n'))
			{
				compact.Span[count++] = b;
			}
		}

		return Base64.DecodeFromUtf8InPlace(compact.Span[..count], out int written) == OperationStatus.Done
			? PinnedBytes.Copy(compact.Span[..written])
			: null;
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
	/// The comment is the last length prefixed field before the padding, and every field in the section of an ordinary
	/// key is length prefixed, so walking them needs no knowledge of the key type. A security key's section holds a
	/// flags byte between its strings, which would stop that walk short, so it is read field by field instead.
	/// </summary>
	private static string ReadComment(ReadOnlySpan<byte> publicBlob, ReadOnlySpan<byte> section)
	{
		SshWireReader blob = new(publicBlob);
		if (blob.TryReadString(out ReadOnlySpan<byte> keyType) && SecurityKeyTypes.IsSecurityKey(Encoding.UTF8.GetString(keyType)))
		{
			return SecurityKeyFile.Parse(publicBlob, section) is { IsValid: true } parsed ? parsed.Key.Comment : "";
		}

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

	/// <summary>One cipher OpenSSH can protect a key file with: AES in counter or CBC mode, or none at all.</summary>
	private sealed record CipherSpec(string Name, int KeyLength, bool Cbc)
	{
		public void Decrypt(ReadOnlySpan<byte> key, ReadOnlySpan<byte> iv, Span<byte> data)
		{
			if (!Cbc)
			{
				AesCtr.Transform(key, iv, data);
				return;
			}

			using Aes aes = Aes.Create();
			byte[] keyCopy = key.ToArray();
			using PinnedBytes plain = new(data.Length);
			try
			{
				aes.Key = keyCopy;
				aes.DecryptCbc(data, iv, plain.Span, PaddingMode.None);
				plain.Span.CopyTo(data);
			}
			finally
			{
				CryptographicOperations.ZeroMemory(keyCopy);
				aes.Clear();
			}
		}
	}
}
