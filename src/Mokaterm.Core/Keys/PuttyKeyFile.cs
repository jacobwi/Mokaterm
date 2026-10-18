using System.Buffers.Binary;
using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Core.Security;
using Org.BouncyCastle.Asn1.Nist;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Math.EC.Rfc8032;
using BouncyInteger = Org.BouncyCastle.Math.BigInteger;
using Integer = System.Numerics.BigInteger;

namespace Mokaterm.Core.Keys;

/// <summary>
/// PuTTY's private key file, formats 2 and 3 (PuTTYgen wrote 2 until 0.74 and writes 3 since 0.75), read from the format
/// as PuTTY's manual publishes it (appendix C). The public half is in the clear. The private half is trusted only once
/// the file's MAC matches, which needs the passphrase for a protected file: format 2 derives its AES-256-CBC key and its
/// HMAC-SHA-1 key from SHA-1 over the passphrase, format 3 derives key, IV and HMAC-SHA-256 key with Argon2. A key that
/// opens is turned into the OpenSSH container the keychain keeps every other key in.
/// </summary>
internal static class PuttyKeyFile
{
	/// <summary>A .ppk past this is not one; a 16384 bit RSA key is under 13 KB.</summary>
	public const int MaxTextLength = 64 * 1024;

	/// <summary>
	/// Argon2 limits for a file that may be hostile: PuTTYgen's default is 8 MiB and a pass count that takes a tenth of a
	/// second, and these leave room for a user who turned both up a long way.
	/// </summary>
	public const int MaxArgon2MemoryKiB = 1024 * 1024;

	/// <inheritdoc cref="MaxArgon2MemoryKiB"/>
	public const int MaxArgon2Passes = 1000;

	/// <inheritdoc cref="MaxArgon2MemoryKiB"/>
	public const int MaxArgon2Parallelism = 64;

	/// <summary>Memory times passes, in KiB: sixteen passes over a gigabyte, or two thousand over PuTTY's default 8 MiB.</summary>
	public const long MaxArgon2Work = 16L * 1024 * 1024;

	private const int MaxLines = 2048;
	private const int MaxNumberDigits = 9;
	private const string EncryptionNone = "none";
	private const string EncryptionAes256Cbc = "aes256-cbc";
	private const int CipherKeyLength = 32;
	private const int Sha1Length = 20;
	private const int Sha256Length = 32;
	private const int V3DerivedLength = CipherKeyLength + AesCbc.BlockSize + Sha256Length;

	private static readonly string[] EcdsaAlgorithms = ["ecdsa-sha2-nistp256", "ecdsa-sha2-nistp384", "ecdsa-sha2-nistp521"];

	private static ReadOnlySpan<byte> Magic => "PuTTY-User-Key-File-"u8;

	private static ReadOnlySpan<byte> MacKeyLabel => "putty-private-key-file-mac-key"u8;

	private static ReadOnlySpan<byte> ByteOrderMark => [0xEF, 0xBB, 0xBF];

	/// <summary>True when the text starts the way every PuTTY key file does, any format.</summary>
	public static bool LooksLikeOne(ReadOnlySpan<byte> text) => TrimStart(text).StartsWith(Magic);

	/// <summary>True for the key types this reader can turn into an OpenSSH key, which are the ones the SSH module signs with.</summary>
	public static bool IsSupportedAlgorithm(string algorithm) =>
		algorithm is SshKeyBlobs.Rsa or SshKeyBlobs.Ed25519 || EcdsaAlgorithms.Contains(algorithm, StringComparer.Ordinal);

	/// <summary>
	/// Reads what a PuTTY key file says about itself without its passphrase: the key type, the comment and the public
	/// key. Nothing here is checked against the MAC, which for a protected file needs the passphrase, so the public key is
	/// what the file claims until it is opened.
	/// </summary>
	public static PrivateKeyStatus TryReadPublicPart(ReadOnlySpan<byte> text, out PuttyPublicPart? part, out string? error)
	{
		part = null;
		using Parsed? file = Parse(text, out PrivateKeyStatus status, out error);
		if (file is null)
		{
			return status;
		}

		part = new PuttyPublicPart(file.Algorithm, file.Comment, file.PublicBlob, file.IsEncrypted);
		return PrivateKeyStatus.Valid;
	}

	/// <summary>
	/// Opens a PuTTY key file: checks the MAC, decrypting first when the file is protected, then turns the key into an
	/// unencrypted OpenSSH key file with the same comment. <paramref name="publicBlob"/> is the checked public key.
	/// </summary>
	public static PrivateKeyStatus TryConvert(ReadOnlySpan<byte> text, string? passphrase, out string? openSshKey, out byte[] publicBlob, out string? error)
	{
		openSshKey = null;
		publicBlob = [];
		using Parsed? file = Parse(text, out PrivateKeyStatus status, out error);
		if (file is null)
		{
			return status;
		}

		if (file.IsEncrypted && string.IsNullOrEmpty(passphrase))
		{
			error = "The key is protected by a passphrase.";
			return PrivateKeyStatus.PassphraseRequired;
		}

		using PinnedBytes password = PinnedBytes.FromString(file.IsEncrypted ? passphrase! : "");
		using PinnedBytes? plain = Open(file, password.Span, out status, out error);
		if (plain is null)
		{
			return status;
		}

		using PinnedBufferWriter openSshPrivate = new(4096);
		if (!TryWriteOpenSshPrivate(file, plain.Span, openSshPrivate, out error))
		{
			return PrivateKeyStatus.Invalid;
		}

		openSshKey = OpenSshPrivateKeyFile.Write(file.PublicBlob, openSshPrivate.WrittenSpan, file.Comment, passphrase: default);
		publicBlob = file.PublicBlob;
		error = null;
		return PrivateKeyStatus.Valid;
	}

	private static ReadOnlySpan<byte> TrimStart(ReadOnlySpan<byte> text)
	{
		// A byte order mark is what Notepad leaves when a file was saved again.
		text = text.StartsWith(ByteOrderMark) ? text[ByteOrderMark.Length..] : text;
		return text.TrimStart(" \t\r\n"u8);
	}

	/// <summary>The header in the order PuTTY writes it, which is the only order PuTTY reads.</summary>
	private static Parsed? Parse(ReadOnlySpan<byte> text, out PrivateKeyStatus status, out string? error)
	{
		status = PrivateKeyStatus.Invalid;
		error = "The PuTTY key file is damaged or incomplete.";
		text = TrimStart(text);
		if (text.Length > MaxTextLength)
		{
			error = "This file is too large to be a PuTTY key.";
			return null;
		}

		LineReader lines = new(text);
		if (!lines.TryRead(out ReadOnlySpan<byte> first) || !first.StartsWith(Magic))
		{
			error = "This is not a PuTTY key file.";
			return null;
		}

		ReadOnlySpan<byte> afterMagic = first[Magic.Length..];
		int colon = afterMagic.IndexOf(": "u8);
		if (colon <= 0)
		{
			return null;
		}

		ReadOnlySpan<byte> versionText = afterMagic[..colon];
		if (!versionText.SequenceEqual("2"u8) && !versionText.SequenceEqual("3"u8))
		{
			status = PrivateKeyStatus.Unsupported;
			error = versionText.SequenceEqual("1"u8)
				? "This PuTTY key is in the first format, from before 2003. Load it in PuTTYgen and save it again."
				: "This PuTTY key is in a format newer than this version of Mokaterm reads.";
			return null;
		}

		int version = versionText[0] - '0';
		string algorithm = Ascii(afterMagic[(colon + 2)..]);
		if (!lines.TryReadHeader("Encryption"u8, out ReadOnlySpan<byte> encryption)
			|| !lines.TryReadHeader("Comment"u8, out ReadOnlySpan<byte> comment)
			|| !lines.TryReadBlock("Public-Lines"u8, out byte[]? publicBlob))
		{
			return null;
		}

		string cipher = Ascii(encryption);
		if (cipher is not (EncryptionNone or EncryptionAes256Cbc))
		{
			status = PrivateKeyStatus.Unsupported;
			error = "The PuTTY key is encrypted in a way Mokaterm cannot read.";
			return null;
		}

		SshWireReader blob = new(publicBlob);
		if (!blob.TryReadString(out ReadOnlySpan<byte> blobAlgorithm) || Ascii(blobAlgorithm) != algorithm)
		{
			return null;
		}

		bool encrypted = cipher == EncryptionAes256Cbc;
		Argon2Parameters? kdf = null;
		if (version == 3 && encrypted)
		{
			kdf = ReadArgon2(ref lines, out status, out error);
			if (kdf is null)
			{
				return null;
			}
		}

		if (!lines.TryReadPinnedBlock("Private-Lines"u8, out PinnedBytes? privateData))
		{
			return null;
		}

		if (!lines.TryReadHeader("Private-MAC"u8, out ReadOnlySpan<byte> macText)
			|| !TryHex(macText, out byte[] mac)
			|| mac.Length != (version == 2 ? Sha1Length : Sha256Length))
		{
			privateData.Dispose();
			return null;
		}

		error = null;
		status = PrivateKeyStatus.Valid;
		return new Parsed
		{
			Version = version,
			Algorithm = algorithm,
			EncryptionName = cipher,
			CommentBytes = comment.ToArray(),
			PublicBlob = publicBlob,
			PrivateData = privateData,
			Mac = mac,
			Kdf = kdf,
		};
	}

	private static Argon2Parameters? ReadArgon2(ref LineReader lines, out PrivateKeyStatus status, out string? error)
	{
		status = PrivateKeyStatus.Invalid;
		error = "The PuTTY key file is damaged or incomplete.";
		if (!lines.TryReadHeader("Key-Derivation"u8, out ReadOnlySpan<byte> flavour)
			|| !lines.TryReadNumber("Argon2-Memory"u8, out int memory)
			|| !lines.TryReadNumber("Argon2-Passes"u8, out int passes)
			|| !lines.TryReadNumber("Argon2-Parallelism"u8, out int parallelism)
			|| !lines.TryReadHeader("Argon2-Salt"u8, out ReadOnlySpan<byte> saltText)
			|| !TryHex(saltText, out byte[] salt))
		{
			return null;
		}

		string name = Ascii(flavour);
		if (name is not ("Argon2id" or "Argon2i" or "Argon2d"))
		{
			status = PrivateKeyStatus.Unsupported;
			error = "The PuTTY key is protected with a key derivation Mokaterm cannot read.";
			return null;
		}

		// Argon2 itself needs a pass, a lane and eight blocks of memory per lane.
		if (passes < 1 || parallelism < 1 || memory < 8 * parallelism || salt.Length == 0)
		{
			return null;
		}

		if (memory > MaxArgon2MemoryKiB || passes > MaxArgon2Passes || parallelism > MaxArgon2Parallelism || (long)memory * passes > MaxArgon2Work)
		{
			status = PrivateKeyStatus.Unsupported;
			error = "The PuTTY key asks for so much Argon2 work that opening it would take minutes. Save it again in PuTTYgen with the default settings.";
			return null;
		}

		error = null;
		status = PrivateKeyStatus.Valid;
		return new Argon2Parameters(name, memory, passes, parallelism, salt);
	}

	/// <summary>Decrypts when needed and checks the MAC. Returns the private blob as PuTTY wrote it, padding included.</summary>
	private static PinnedBytes? Open(Parsed file, ReadOnlySpan<byte> passphrase, out PrivateKeyStatus status, out string? error)
	{
		PinnedBytes plain = PinnedBytes.Copy(file.PrivateData.Span);
		try
		{
			using PinnedBytes macKey = file.Version == 2 ? DeriveV2MacKey(passphrase) : new PinnedBytes(Sha256Length);
			int macKeyLength = file.Version == 2 ? Sha1Length : 0;
			if (file.IsEncrypted)
			{
				if (plain.Length == 0 || plain.Length % AesCbc.BlockSize != 0)
				{
					status = PrivateKeyStatus.Invalid;
					error = "The PuTTY key file is damaged or incomplete.";
					plain.Dispose();
					return null;
				}

				if (file.Version == 2)
				{
					using PinnedBytes key = DeriveV2CipherKey(passphrase);
					Span<byte> iv = stackalloc byte[AesCbc.BlockSize];
					iv.Clear();
					AesCbc.Decrypt(key.Span, iv, plain.Span);
				}
				else
				{
					using PinnedBytes derived = DeriveV3(file.Kdf!, passphrase);
					AesCbc.Decrypt(derived.Span[..CipherKeyLength], derived.Span.Slice(CipherKeyLength, AesCbc.BlockSize), plain.Span);
					derived.Span[(CipherKeyLength + AesCbc.BlockSize)..].CopyTo(macKey.Span);
					macKeyLength = Sha256Length;
				}
			}

			if (!MacMatches(file, macKey.Span[..macKeyLength], plain.Span))
			{
				// A wrong passphrase gives the wrong MAC key and garbage to check it against, both of which land here.
				status = file.IsEncrypted ? PrivateKeyStatus.WrongPassphrase : PrivateKeyStatus.Invalid;
				error = file.IsEncrypted ? "The passphrase is wrong." : "The PuTTY key file has been changed or damaged: its MAC does not match.";
				plain.Dispose();
				return null;
			}

			status = PrivateKeyStatus.Valid;
			error = null;
			return plain;
		}
		catch
		{
			plain.Dispose();
			throw;
		}
	}

	/// <summary>
	/// The MAC covers the key type, the encryption, the comment, the public blob and the decrypted private blob, each as
	/// an SSH string.
	/// </summary>
	[SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms", Justification = "PuTTY's format 2 defines its MAC as HMAC-SHA-1; it cannot be checked with anything else.")]
	private static bool MacMatches(Parsed file, ReadOnlySpan<byte> macKey, ReadOnlySpan<byte> plainPrivate)
	{
		using PinnedBufferWriter data = new(plainPrivate.Length + file.PublicBlob.Length + file.CommentBytes.Length + 128);
		SshWire.WriteString(data, file.Algorithm);
		SshWire.WriteString(data, file.EncryptionName);
		SshWire.WriteString(data, file.CommentBytes);
		SshWire.WriteString(data, file.PublicBlob);
		SshWire.WriteString(data, plainPrivate);
		Span<byte> mac = stackalloc byte[Sha256Length];
		int written = file.Version == 2
			? HMACSHA1.HashData(macKey, data.WrittenSpan, mac)
			: HMACSHA256.HashData(macKey, data.WrittenSpan, mac);
		return CryptographicOperations.FixedTimeEquals(mac[..written], file.Mac);
	}

	/// <summary>Format 2: SHA-1 over the label and the passphrase, empty for a file without one.</summary>
	[SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms", Justification = "PuTTY's format 2 derives its MAC key with SHA-1.")]
	private static PinnedBytes DeriveV2MacKey(ReadOnlySpan<byte> passphrase)
	{
		using PinnedBytes input = new(MacKeyLabel.Length + passphrase.Length);
		MacKeyLabel.CopyTo(input.Span);
		passphrase.CopyTo(input.Span[MacKeyLabel.Length..]);
		PinnedBytes key = new(Sha256Length);
		_ = SHA1.HashData(input.Span, key.Span);
		return key;
	}

	/// <summary>Format 2: SHA-1 of a 32 bit counter and the passphrase, for counters 0 and 1, cut to 32 bytes. The IV is zero.</summary>
	[SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms", Justification = "PuTTY's format 2 derives its AES key with SHA-1.")]
	private static PinnedBytes DeriveV2CipherKey(ReadOnlySpan<byte> passphrase)
	{
		using PinnedBytes input = new(4 + passphrase.Length);
		passphrase.CopyTo(input.Span[4..]);
		using PinnedBytes digests = new(2 * Sha1Length);
		for (uint counter = 0; counter < 2; counter++)
		{
			BinaryPrimitives.WriteUInt32BigEndian(input.Span, counter);
			_ = SHA1.HashData(input.Span, digests.Span[((int)counter * Sha1Length)..]);
		}

		return PinnedBytes.Copy(digests.Span[..CipherKeyLength]);
	}

	/// <summary>Format 3: Argon2 over the passphrase gives 80 bytes, the AES key, the IV and the MAC key in that order.</summary>
	private static PinnedBytes DeriveV3(Argon2Parameters kdf, ReadOnlySpan<byte> passphrase)
	{
		using PinnedBytes password = PinnedBytes.Copy(passphrase);
		using Argon2 argon2 = kdf.Flavour switch
		{
			"Argon2i" => new Argon2i(password.Array),
			"Argon2d" => new Argon2d(password.Array),
			_ => new Argon2id(password.Array),
		};

		argon2.Salt = kdf.Salt;
		argon2.MemorySize = kdf.MemoryKiB;
		argon2.Iterations = kdf.Passes;
		argon2.DegreeOfParallelism = kdf.Parallelism;
		byte[] derived = argon2.GetBytes(V3DerivedLength);
		try
		{
			return PinnedBytes.Copy(derived);
		}
		finally
		{
			CryptographicOperations.ZeroMemory(derived);
		}
	}

	/// <summary>
	/// Reads the private blob for the key type and writes OpenSSH's private fields, checking that the private half belongs
	/// to the public half: the MAC proves the file is what PuTTY wrote, this proves it was read the way PuTTY meant.
	/// </summary>
	private static bool TryWriteOpenSshPrivate(Parsed file, ReadOnlySpan<byte> plain, PinnedBufferWriter writer, out string? error)
	{
		error = "The PuTTY key file is damaged or incomplete.";
		SshWireReader publicReader = new(file.PublicBlob);
		SshWireReader privateReader = new(plain);
		_ = publicReader.TryReadString(out _);
		if (!IsSupportedAlgorithm(file.Algorithm))
		{
			error = UnsupportedAlgorithm(file.Algorithm);
			return false;
		}

		if (file.Algorithm == SshKeyBlobs.Rsa)
		{
			if (!publicReader.TryReadString(out ReadOnlySpan<byte> e)
				|| !publicReader.TryReadString(out ReadOnlySpan<byte> n)
				|| !privateReader.TryReadString(out ReadOnlySpan<byte> d)
				|| !privateReader.TryReadString(out ReadOnlySpan<byte> p)
				|| !privateReader.TryReadString(out ReadOnlySpan<byte> q)
				|| !privateReader.TryReadString(out ReadOnlySpan<byte> iqmp)
				|| !RsaFieldsAgree(e, n, d, p, q, iqmp))
			{
				return false;
			}

			SshKeyBlobs.WriteRsaPrivate(writer, n, e, d, iqmp, p, q);
			error = null;
			return true;
		}

		if (file.Algorithm == SshKeyBlobs.Ed25519)
		{
			// PuTTY keeps the 32 byte seed as a little endian number with its top zero bytes left off.
			if (!publicReader.TryReadString(out ReadOnlySpan<byte> publicKey)
				|| publicKey.Length != SshKeyBlobs.Ed25519KeyLength
				|| !privateReader.TryReadString(out ReadOnlySpan<byte> stored)
				|| stored.Length is 0 or > SshKeyBlobs.Ed25519KeyLength)
			{
				return false;
			}

			using PinnedBytes seed = new(SshKeyBlobs.Ed25519KeyLength);
			stored.CopyTo(seed.Span);
			byte[] derived = new byte[Ed25519.PublicKeySize];
			Ed25519.GeneratePublicKey(seed.Array, 0, derived, 0);
			if (!derived.AsSpan().SequenceEqual(publicKey))
			{
				return false;
			}

			SshKeyBlobs.WriteEd25519Private(writer, publicKey, seed.Span);
			error = null;
			return true;
		}

		if (!publicReader.TryReadString(out ReadOnlySpan<byte> curve)
			|| file.Algorithm != "ecdsa-sha2-" + Ascii(curve)
			|| !publicReader.TryReadString(out ReadOnlySpan<byte> point)
			|| !privateReader.TryReadString(out ReadOnlySpan<byte> scalar)
			|| !EcdsaPointAgrees(Ascii(curve), point, scalar))
		{
			return false;
		}

		SshKeyBlobs.WriteEcdsaPrivate(writer, file.Algorithm, Ascii(curve), point, scalar);
		error = null;
		return true;
	}

	/// <summary>The wording for a key type the SSH module does not sign with.</summary>
	public static string UnsupportedAlgorithm(string algorithm) => algorithm switch
	{
		"ssh-dss" => "DSA keys are not supported: OpenSSH dropped them, and so did the SSH library Mokaterm uses.",
		"ssh-ed448" => "Ed448 keys are not supported.",
		_ => "This key type is not supported.",
	};

	private static bool RsaFieldsAgree(
		ReadOnlySpan<byte> e,
		ReadOnlySpan<byte> n,
		ReadOnlySpan<byte> d,
		ReadOnlySpan<byte> p,
		ReadOnlySpan<byte> q,
		ReadOnlySpan<byte> iqmp)
	{
		if (!TryPositive(e, out Integer exponent)
			|| !TryPositive(n, out Integer modulus)
			|| !TryPositive(d, out Integer privateExponent)
			|| !TryPositive(p, out Integer primeP)
			|| !TryPositive(q, out Integer primeQ)
			|| !TryPositive(iqmp, out Integer inverseQ)
			|| primeP <= Integer.One
			|| primeQ <= Integer.One)
		{
			return false;
		}

		Integer pMinusOne = primeP - Integer.One;
		Integer qMinusOne = primeQ - Integer.One;
		Integer lambda = pMinusOne / Integer.GreatestCommonDivisor(pMinusOne, qMinusOne) * qMinusOne;
		return modulus == primeP * primeQ
			&& (primeQ * inverseQ) % primeP == Integer.One
			&& (exponent * privateExponent) % lambda == Integer.One;
	}

	private static bool TryPositive(ReadOnlySpan<byte> mpint, out Integer value)
	{
		// An mpint with its top bit set is negative, which no key field is.
		value = default;
		if (mpint.IsEmpty || (mpint[0] & 0x80) != 0)
		{
			return false;
		}

		value = new Integer(mpint, isUnsigned: true, isBigEndian: true);
		return value.Sign > 0;
	}

	private static bool EcdsaPointAgrees(string curve, ReadOnlySpan<byte> point, ReadOnlySpan<byte> scalar)
	{
		X9ECParameters? parameters = curve switch
		{
			"nistp256" => NistNamedCurves.GetByName("P-256"),
			"nistp384" => NistNamedCurves.GetByName("P-384"),
			"nistp521" => NistNamedCurves.GetByName("P-521"),
			_ => null,
		};

		if (parameters is null || scalar.IsEmpty || (scalar[0] & 0x80) != 0)
		{
			return false;
		}

		BouncyInteger d = new(1, scalar.ToArray());
		if (d.SignValue <= 0 || d.CompareTo(parameters.N) >= 0)
		{
			return false;
		}

		byte[] expected = parameters.G.Multiply(d).Normalize().GetEncoded(false);
		return expected.AsSpan().SequenceEqual(point);
	}

	private static string Ascii(ReadOnlySpan<byte> value) => Encoding.ASCII.GetString(value);

	private static bool TryHex(ReadOnlySpan<byte> text, out byte[] value)
	{
		value = [];
		if (text.IsEmpty || text.Length % 2 != 0)
		{
			return false;
		}

		try
		{
			value = Convert.FromHexString(Encoding.ASCII.GetString(text));
			return true;
		}
		catch (FormatException)
		{
			return false;
		}
	}

	/// <summary>The lines of a key file, read as <c>Name: value</c> headers and base64 blocks.</summary>
	private ref struct LineReader
	{
		private ReadOnlySpan<byte> _rest;

		public LineReader(ReadOnlySpan<byte> text) => _rest = text;

		public bool TryRead(out ReadOnlySpan<byte> line)
		{
			if (_rest.IsEmpty)
			{
				line = default;
				return false;
			}

			int end = _rest.IndexOf((byte)'\n');
			line = end < 0 ? _rest : _rest[..end];
			_rest = end < 0 ? default : _rest[(end + 1)..];
			if (!line.IsEmpty && line[^1] == '\r')
			{
				line = line[..^1];
			}

			return true;
		}

		public bool TryReadHeader(ReadOnlySpan<byte> name, out ReadOnlySpan<byte> value)
		{
			value = default;
			if (!TryRead(out ReadOnlySpan<byte> line)
				|| line.Length < name.Length + 2
				|| !line.StartsWith(name)
				|| !line[name.Length..].StartsWith(": "u8))
			{
				return false;
			}

			value = line[(name.Length + 2)..];
			return true;
		}

		public bool TryReadNumber(ReadOnlySpan<byte> name, out int value)
		{
			value = 0;
			return TryReadHeader(name, out ReadOnlySpan<byte> text)
				&& text.Length is > 0 and <= MaxNumberDigits
				&& text.IndexOfAnyExceptInRange((byte)'0', (byte)'9') < 0
				&& int.TryParse(Ascii(text), NumberStyles.None, CultureInfo.InvariantCulture, out value);
		}

		public bool TryReadBlock(ReadOnlySpan<byte> name, [NotNullWhen(true)] out byte[]? value)
		{
			value = null;
			if (!TryReadPinnedBlock(name, out PinnedBytes? block))
			{
				return false;
			}

			using (block)
			{
				value = block.Span.ToArray();
				return true;
			}
		}

		/// <summary>A count line and that many base64 lines, decoded into pinned memory: an unencrypted private block is secret.</summary>
		public bool TryReadPinnedBlock(ReadOnlySpan<byte> name, [NotNullWhen(true)] out PinnedBytes? value)
		{
			value = null;
			if (!TryReadNumber(name, out int count) || count is < 1 or > MaxLines)
			{
				return false;
			}

			ReadOnlySpan<byte> start = _rest;
			int length = 0;
			for (int line = 0; line < count; line++)
			{
				if (!TryRead(out ReadOnlySpan<byte> text) || text.IsEmpty)
				{
					return false;
				}

				length += text.Length;
			}

			using PinnedBytes encoded = new(length);
			int written = 0;
			LineReader again = new(start);
			for (int line = 0; line < count; line++)
			{
				_ = again.TryRead(out ReadOnlySpan<byte> text);
				text.CopyTo(encoded.Span[written..]);
				written += text.Length;
			}

			PinnedBytes decoded = new(Base64.GetMaxDecodedFromUtf8Length(length));
			if (Base64.DecodeFromUtf8(encoded.Span, decoded.Span, out _, out int bytes) != System.Buffers.OperationStatus.Done || bytes == 0)
			{
				decoded.Dispose();
				return false;
			}

			value = PinnedBytes.Copy(decoded.Span[..bytes]);
			decoded.Dispose();
			return true;
		}
	}

	private sealed record Argon2Parameters(string Flavour, int MemoryKiB, int Passes, int Parallelism, byte[] Salt);

	/// <summary>A parsed file. The private block is still encrypted when the file is, and is wiped on dispose either way.</summary>
	private sealed class Parsed : IDisposable
	{
		public required int Version { get; init; }

		public required string Algorithm { get; init; }

		public required string EncryptionName { get; init; }

		public required byte[] CommentBytes { get; init; }

		public required byte[] PublicBlob { get; init; }

		public required PinnedBytes PrivateData { get; init; }

		public required byte[] Mac { get; init; }

		public Argon2Parameters? Kdf { get; init; }

		public bool IsEncrypted => EncryptionName != EncryptionNone;

		/// <summary>PuTTY writes the comment as it was typed, which PuTTYgen keeps in UTF-8.</summary>
		public string Comment => Encoding.UTF8.GetString(CommentBytes);

		public void Dispose() => PrivateData.Dispose();
	}
}
