using System.Text;
using Mokaterm.Core.Keys;

namespace Mokaterm.Core.Tests.Keys;

/// <summary>An openssh-key-v1 container taken apart, so a test can check what was written field by field.</summary>
internal sealed record ParsedOpenSshKey(string Cipher, string Kdf, byte[] Salt, int Rounds, byte[] PublicBlob, byte[] Section);

/// <summary>
/// Reads the container the generator writes, on its own rather than through the writer, so the tests check the
/// bytes that reach the file instead of checking the writer against itself.
/// </summary>
internal static class OpenSshKeyParser
{
	public static ParsedOpenSshKey Parse(string text)
	{
		int start = text.IndexOf("-----BEGIN OPENSSH PRIVATE KEY-----", StringComparison.Ordinal);
		int end = text.IndexOf("-----END OPENSSH PRIVATE KEY-----", StringComparison.Ordinal);
		Assert.True(start == 0, "The file must start with the OpenSSH marker.");
		Assert.True(end > start, "The file must end with the OpenSSH marker.");

		byte[] blob = Convert.FromBase64String(text[(start + 35)..end]);
		Assert.Equal("openssh-key-v1\0", Encoding.ASCII.GetString(blob, 0, 15));

		SshWireReader reader = new(blob.AsSpan(15));
		Assert.True(reader.TryReadString(out ReadOnlySpan<byte> cipher));
		Assert.True(reader.TryReadString(out ReadOnlySpan<byte> kdf));
		Assert.True(reader.TryReadString(out ReadOnlySpan<byte> options));
		Assert.True(reader.TryReadUInt32(out uint count));
		Assert.Equal(1u, count);
		Assert.True(reader.TryReadString(out ReadOnlySpan<byte> publicBlob));
		Assert.True(reader.TryReadString(out ReadOnlySpan<byte> section));
		Assert.Equal(0, reader.Remaining);

		byte[] salt = [];
		int rounds = 0;
		if (!options.IsEmpty)
		{
			SshWireReader optionReader = new(options);
			Assert.True(optionReader.TryReadString(out ReadOnlySpan<byte> saltBytes));
			Assert.True(optionReader.TryReadUInt32(out uint roundCount));
			salt = saltBytes.ToArray();
			rounds = (int)roundCount;
		}

		return new ParsedOpenSshKey(
			Encoding.ASCII.GetString(cipher),
			Encoding.ASCII.GetString(kdf),
			salt,
			rounds,
			publicBlob.ToArray(),
			section.ToArray());
	}

	/// <summary>Undoes the aes256-ctr layer with the same primitives OpenSSH uses, both checked by their own tests.</summary>
	public static byte[] Decrypt(ParsedOpenSshKey key, string passphrase)
	{
		byte[] keyAndIv = new byte[48];
		BcryptPbkdf.DeriveKey(Encoding.UTF8.GetBytes(passphrase), key.Salt, key.Rounds, keyAndIv);
		byte[] section = [.. key.Section];
		AesCtr.Transform(keyAndIv.AsSpan(0, 32), keyAndIv.AsSpan(32), section);
		return section;
	}

	/// <summary>Reads the private section: the two check words, then the key type and its fields, then the comment.</summary>
	public static (string KeyType, List<byte[]> Fields, string Comment) ReadSection(byte[] section)
	{
		SshWireReader reader = new(section);
		Assert.True(reader.TryReadUInt32(out uint first));
		Assert.True(reader.TryReadUInt32(out uint second));
		Assert.Equal(first, second);

		List<byte[]> fields = [];
		while (reader.TryReadString(out ReadOnlySpan<byte> field))
		{
			fields.Add(field.ToArray());
		}

		Assert.True(fields.Count >= 2, "A private section holds a key type, at least one field and a comment.");
		string keyType = Encoding.ASCII.GetString(fields[0]);
		string comment = Encoding.UTF8.GetString(fields[^1]);
		return (keyType, fields[1..^1], comment);
	}
}
