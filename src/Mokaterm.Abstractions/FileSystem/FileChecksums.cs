using System.Buffers;
using System.Security.Cryptography;

namespace Mokaterm.Abstractions.FileSystem;

/// <summary>
/// Checksums in the one form every side of a transfer uses: lower-case hex. A server's answer, a hash computed while the
/// bytes passed and a local file read back all go through here, so they compare as plain strings.
/// </summary>
public static class FileChecksums
{
	private const int ReadBufferSize = 128 * 1024;

	/// <summary>The name to show for an algorithm, the way checksum tools print it.</summary>
	public static string Name(ChecksumAlgorithm algorithm) => algorithm switch
	{
		ChecksumAlgorithm.Sha256 => "SHA-256",
		ChecksumAlgorithm.Sha512 => "SHA-512",
		_ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, null),
	};

	/// <summary>Hex digits in a checksum of <paramref name="algorithm"/>.</summary>
	public static int HexLength(ChecksumAlgorithm algorithm) => algorithm switch
	{
		ChecksumAlgorithm.Sha256 => 64,
		ChecksumAlgorithm.Sha512 => 128,
		_ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, null),
	};

	public static IncrementalHash CreateHash(ChecksumAlgorithm algorithm) => IncrementalHash.CreateHash(algorithm switch
	{
		ChecksumAlgorithm.Sha256 => HashAlgorithmName.SHA256,
		ChecksumAlgorithm.Sha512 => HashAlgorithmName.SHA512,
		_ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, null),
	});

	/// <summary>Finishes <paramref name="hash"/> and returns it as lower-case hex.</summary>
	public static string ToHex(IncrementalHash hash)
	{
		ArgumentNullException.ThrowIfNull(hash);
		return Convert.ToHexStringLower(hash.GetHashAndReset());
	}

	/// <summary>Reads <paramref name="source"/> to its end and returns its checksum.</summary>
	public static async Task<string> ComputeAsync(Stream source, ChecksumAlgorithm algorithm, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(source);
		using IncrementalHash hash = CreateHash(algorithm);
		byte[] buffer = ArrayPool<byte>.Shared.Rent(ReadBufferSize);
		try
		{
			int read;
			while ((read = await source.ReadAsync(buffer.AsMemory(0, ReadBufferSize), cancellationToken)) > 0)
			{
				hash.AppendData(buffer, 0, read);
			}
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(buffer);
		}

		return ToHex(hash);
	}

	/// <summary>
	/// The checksum at the start of what a server sent, lower-cased, or null when it does not start with the right number
	/// of hex digits. Tools print the hash first and then a file name (<c>sha256sum</c>) or nothing (<c>sha256 -q</c>), and
	/// FTP replies may use upper case.
	/// </summary>
	public static string? Parse(ChecksumAlgorithm algorithm, string? reply)
	{
		if (reply is null)
		{
			return null;
		}

		ReadOnlySpan<char> text = reply.AsSpan().TrimStart();
		int length = HexLength(algorithm);
		if (text.Length < length || !IsHex(text[..length]) || (text.Length > length && char.IsAsciiHexDigit(text[length])))
		{
			return null;
		}

		return text[..length].ToString().ToLowerInvariant();
	}

	private static bool IsHex(ReadOnlySpan<char> text)
	{
		foreach (char character in text)
		{
			if (!char.IsAsciiHexDigit(character))
			{
				return false;
			}
		}

		return true;
	}
}
