using System.Buffers.Binary;
using System.Buffers.Text;
using System.Security.Cryptography;

namespace Mokaterm.Modules.Ssh.Keys;

internal enum PrivateKeyFormat
{
	Unknown,
	PublicKey,
	OpenSsh,
	Pem,
	Pkcs8,
	SshCom,
	PuTTY,
	Unsupported,
}

/// <summary>
/// What a pasted key looks like before SSH.NET parses it: its format, whether it is encrypted and whether its structure
/// is complete. That lets a missing passphrase, a wrong one and a damaged key each get their own message.
/// </summary>
internal readonly record struct PrivateKeyEnvelope(PrivateKeyFormat Format, bool IsEncrypted)
{
	private const uint SshComMagic = 0x3F6FF9EB;
	private const int DesBlockSize = 8;
	private const int AesBlockSize = 16;

	public static ReadOnlySpan<byte> Whitespace => " \t\r\n"u8;

	private static ReadOnlySpan<byte> PemBegin => "-----BEGIN "u8;

	private static ReadOnlySpan<byte> OpenSshMagic => "openssh-key-v1\0"u8;

	public static PrivateKeyEnvelope Detect(ReadOnlySpan<byte> text)
	{
		text = text.Trim(Whitespace);
		if (text.StartsWith("PuTTY-User-Key-File-"u8))
		{
			return new(PrivateKeyFormat.PuTTY, text.IndexOf("Encryption: none"u8) < 0);
		}

		if (text.StartsWith("ssh-"u8) || text.StartsWith("ecdsa-"u8) || text.StartsWith("sk-"u8))
		{
			return new(PrivateKeyFormat.PublicKey, false);
		}

		if (!text.StartsWith(PemBegin))
		{
			return default;
		}

		ReadOnlySpan<byte> afterBegin = text[PemBegin.Length..];
		int labelEnd = afterBegin.IndexOf("-----"u8);
		int lineEnd = text.IndexOf((byte)'\n');
		if (labelEnd <= 0 || lineEnd < 0)
		{
			return default;
		}

		ReadOnlySpan<byte> label = afterBegin[..labelEnd];
		ReadOnlySpan<byte> body = text[(lineEnd + 1)..];
		int end = body.IndexOf("-----END"u8);
		if (end < 0)
		{
			return default;
		}

		body = body[..end];
		if (label.SequenceEqual("OPENSSH PRIVATE KEY"u8))
		{
			return ReadOpenSsh(body);
		}

		if (label.SequenceEqual("RSA PRIVATE KEY"u8) || label.SequenceEqual("EC PRIVATE KEY"u8))
		{
			return ReadPem(body);
		}

		if (label.SequenceEqual("PRIVATE KEY"u8))
		{
			return new(PrivateKeyFormat.Pkcs8, false);
		}

		if (label.SequenceEqual("ENCRYPTED PRIVATE KEY"u8))
		{
			return new(PrivateKeyFormat.Pkcs8, true);
		}

		if (label.SequenceEqual("SSH2 ENCRYPTED PRIVATE KEY"u8))
		{
			return ReadSshCom(body);
		}

		return label.EndsWith("PRIVATE KEY"u8) ? new(PrivateKeyFormat.Unsupported, false) : default;
	}

	// magic, string cipher, string kdf, string kdf options, uint32 key count, that many public keys, string private section
	private static PrivateKeyEnvelope ReadOpenSsh(ReadOnlySpan<byte> body) => WithDecoded(body, static data =>
	{
		if (!data.StartsWith(OpenSshMagic))
		{
			return default;
		}

		ReadOnlySpan<byte> rest = data[OpenSshMagic.Length..];
		if (!TryReadString(ref rest, out ReadOnlySpan<byte> cipher)
			|| !TryReadString(ref rest, out _)
			|| !TryReadString(ref rest, out _)
			|| rest.Length < 4)
		{
			return default;
		}

		uint keyCount = BinaryPrimitives.ReadUInt32BigEndian(rest);
		rest = rest[4..];
		for (uint i = 0; i < keyCount; i++)
		{
			if (!TryReadString(ref rest, out _))
			{
				return default;
			}
		}

		bool encrypted = !cipher.SequenceEqual("none"u8);
		return TryReadString(ref rest, out ReadOnlySpan<byte> privateSection) && (!encrypted || privateSection.Length % DesBlockSize == 0)
			? new(PrivateKeyFormat.OpenSsh, encrypted)
			: default;
	});

	// Optional "Proc-Type: 4,ENCRYPTED" and "DEK-Info: CIPHER,IV" headers, a blank line, then the base64 body.
	private static PrivateKeyEnvelope ReadPem(ReadOnlySpan<byte> body)
	{
		if (body.IndexOf("Proc-Type: 4,ENCRYPTED"u8) < 0)
		{
			return new(PrivateKeyFormat.Pem, false);
		}

		int headersEnd = body.IndexOf("\n\n"u8) is >= 0 and int lf ? lf + 2
			: body.IndexOf("\r\n\r\n"u8) is >= 0 and int crlf ? crlf + 4
			: -1;

		if (headersEnd < 0)
		{
			return default;
		}

		int blockSize = body.IndexOf("DEK-Info: AES"u8) >= 0 ? AesBlockSize : DesBlockSize;
		return WithDecoded(body[headersEnd..], data => data.Length > 0 && data.Length % blockSize == 0 ? new(PrivateKeyFormat.Pem, true) : default);
	}

	// uint32 magic, uint32 total length, string key type, string cipher
	private static PrivateKeyEnvelope ReadSshCom(ReadOnlySpan<byte> body) => WithDecoded(body, static data =>
	{
		if (data.Length < 8 || BinaryPrimitives.ReadUInt32BigEndian(data) != SshComMagic)
		{
			return default;
		}

		ReadOnlySpan<byte> rest = data[8..];
		return TryReadString(ref rest, out _) && TryReadString(ref rest, out ReadOnlySpan<byte> cipher)
			? new(PrivateKeyFormat.SshCom, !cipher.SequenceEqual("none"u8))
			: default;
	});

	/// <summary>Decodes the base64 body into pinned memory that is wiped afterwards, since an unencrypted key is secret.</summary>
	private static PrivateKeyEnvelope WithDecoded(ReadOnlySpan<byte> base64, DecodedReader read)
	{
		byte[] buffer = GC.AllocateArray<byte>(base64.Length, pinned: true);
		try
		{
			int count = 0;
			foreach (byte b in base64)
			{
				if (!Whitespace.Contains(b))
				{
					buffer[count++] = b;
				}
			}

			return Base64.DecodeFromUtf8InPlace(buffer.AsSpan(0, count), out int written) == System.Buffers.OperationStatus.Done
				? read(buffer.AsSpan(0, written))
				: default;
		}
		finally
		{
			CryptographicOperations.ZeroMemory(buffer);
		}
	}

	private static bool TryReadString(ref ReadOnlySpan<byte> data, out ReadOnlySpan<byte> value)
	{
		value = default;
		if (data.Length < 4)
		{
			return false;
		}

		uint length = BinaryPrimitives.ReadUInt32BigEndian(data);
		if (length > (uint)(data.Length - 4))
		{
			return false;
		}

		value = data.Slice(4, (int)length);
		data = data[(4 + (int)length)..];
		return true;
	}

	private delegate PrivateKeyEnvelope DecodedReader(ReadOnlySpan<byte> data);
}
