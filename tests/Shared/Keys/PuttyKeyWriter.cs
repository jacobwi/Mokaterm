using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace Mokaterm.Tests.Shared;

/// <summary>
/// Writes PuTTY key files the way PuTTYgen does, built from the format PuTTY's manual publishes (appendix C), because no
/// machine that runs these tests has puttygen. A file from here checks Core against our own reading of that
/// specification, not against PuTTY; SSH.NET opening the same files is the second, independent reading.
/// </summary>
internal static class PuttyKeyWriter
{
	private const int BlockSize = 16;
	private const int LineLength = 64;

	/// <summary>Argon2 settings for a format 3 file. Tiny by default, so the tests stay fast.</summary>
	public sealed record Argon2Settings(string Flavour = "Argon2id", int MemoryKiB = 64, int Passes = 1, int Parallelism = 1);

	/// <summary>
	/// A whole .ppk. <paramref name="passphrase"/> null or empty writes an unencrypted file. <paramref name="tamper"/>
	/// changes the decrypted private blob after the MAC was taken over it, for files whose MAC has to fail.
	/// </summary>
	[SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms", Justification = "PuTTY's format 2 is built on SHA-1.")]
	public static string Write(
		int version,
		TestKeyPair key,
		string comment,
		string? passphrase,
		Argon2Settings? argon2 = null,
		string lineEnding = "\r\n",
		byte[]? privateBlob = null,
		Action<byte[]>? tamper = null)
	{
		bool encrypted = !string.IsNullOrEmpty(passphrase);
		string encryption = encrypted ? "aes256-cbc" : "none";
		byte[] password = Encoding.UTF8.GetBytes(passphrase ?? "");
		byte[] plain = Pad(privateBlob ?? key.PuttyPrivateBlob, encrypted, version);

		byte[] macKey;
		byte[] cipherKey = [];
		byte[] iv = new byte[BlockSize];
		string kdfLines = "";
		if (version == 2)
		{
			macKey = SHA1.HashData([.. "putty-private-key-file-mac-key"u8, .. password]);
			if (encrypted)
			{
				byte[] first = SHA1.HashData([0, 0, 0, 0, .. password]);
				byte[] second = SHA1.HashData([0, 0, 0, 1, .. password]);
				cipherKey = [.. first, .. second.AsSpan(0, 12)];
			}
		}
		else if (encrypted)
		{
			Argon2Settings settings = argon2 ?? new Argon2Settings();
			byte[] salt = RandomNumberGenerator.GetBytes(16);
			byte[] derived = Derive(settings, password, salt);
			cipherKey = derived[..32];
			iv = derived[32..48];
			macKey = derived[48..80];
			kdfLines =
				$"Key-Derivation: {settings.Flavour}{lineEnding}" +
				$"Argon2-Memory: {settings.MemoryKiB}{lineEnding}" +
				$"Argon2-Passes: {settings.Passes}{lineEnding}" +
				$"Argon2-Parallelism: {settings.Parallelism}{lineEnding}" +
				$"Argon2-Salt: {Convert.ToHexStringLower(salt)}{lineEnding}";
		}
		else
		{
			macKey = [];
		}

		byte[] macData = [.. String(Encoding.ASCII.GetBytes(key.Algorithm)), .. String(Encoding.ASCII.GetBytes(encryption)), .. String(Encoding.UTF8.GetBytes(comment)), .. String(key.PublicBlob), .. String(plain)];
		byte[] mac = version == 2 ? HMACSHA1.HashData(macKey, macData) : HMACSHA256.HashData(macKey, macData);
		tamper?.Invoke(plain);

		byte[] stored = plain;
		if (encrypted)
		{
			using Aes aes = Aes.Create();
			aes.Key = cipherKey;
			stored = aes.EncryptCbc(plain, iv, PaddingMode.None);
		}

		StringBuilder text = new();
		text.Append(CultureInfo.InvariantCulture, $"PuTTY-User-Key-File-{version}: {key.Algorithm}{lineEnding}");
		text.Append(CultureInfo.InvariantCulture, $"Encryption: {encryption}{lineEnding}");
		text.Append(CultureInfo.InvariantCulture, $"Comment: {comment}{lineEnding}");
		AppendBlock(text, "Public-Lines", key.PublicBlob, lineEnding);
		text.Append(kdfLines);
		AppendBlock(text, "Private-Lines", stored, lineEnding);
		text.Append(CultureInfo.InvariantCulture, $"Private-MAC: {Convert.ToHexStringLower(mac)}{lineEnding}");
		return text.ToString();
	}

	/// <summary>An SSH string: a 32 bit big endian length, then the bytes.</summary>
	public static byte[] String(ReadOnlySpan<byte> value)
	{
		byte[] result = new byte[4 + value.Length];
		BinaryPrimitives.WriteUInt32BigEndian(result, (uint)value.Length);
		value.CopyTo(result.AsSpan(4));
		return result;
	}

	/// <summary>An SSH mpint from an unsigned big endian number: leading zeros dropped, one put back for a set top bit.</summary>
	public static byte[] MpInt(ReadOnlySpan<byte> magnitude)
	{
		ReadOnlySpan<byte> trimmed = magnitude.TrimStart((byte)0);
		return trimmed.IsEmpty ? String([]) : (trimmed[0] & 0x80) != 0 ? String([0, .. trimmed]) : String(trimmed);
	}

	private static byte[] Derive(Argon2Settings settings, byte[] password, byte[] salt)
	{
		using Argon2 argon2 = settings.Flavour switch
		{
			"Argon2i" => new Argon2i(password),
			"Argon2d" => new Argon2d(password),
			_ => new Argon2id(password),
		};

		argon2.Salt = salt;
		argon2.MemorySize = settings.MemoryKiB;
		argon2.Iterations = settings.Passes;
		argon2.DegreeOfParallelism = settings.Parallelism;
		return argon2.GetBytes(80);
	}

	/// <summary>
	/// An encrypted private blob is padded to whole AES blocks: format 2 with the start of the blob's SHA-1, format 3 with
	/// random bytes. An unencrypted one is left as it is.
	/// </summary>
	[SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms", Justification = "PuTTY's format 2 pads with SHA-1.")]
	private static byte[] Pad(byte[] blob, bool encrypted, int version)
	{
		if (!encrypted || blob.Length % BlockSize == 0)
		{
			return [.. blob];
		}

		int padding = BlockSize - (blob.Length % BlockSize);
		byte[] filler = version == 2 ? SHA1.HashData(blob)[..padding] : RandomNumberGenerator.GetBytes(padding);
		return [.. blob, .. filler];
	}

	private static void AppendBlock(StringBuilder text, string name, byte[] data, string lineEnding)
	{
		string encoded = Convert.ToBase64String(data);
		int lines = (encoded.Length + LineLength - 1) / LineLength;
		text.Append(CultureInfo.InvariantCulture, $"{name}: {lines}{lineEnding}");
		for (int offset = 0; offset < encoded.Length; offset += LineLength)
		{
			text.Append(encoded, offset, Math.Min(LineLength, encoded.Length - offset)).Append(lineEnding);
		}
	}
}
