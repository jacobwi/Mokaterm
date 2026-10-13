using System.Buffers;
using System.Buffers.Text;
using Mokaterm.Abstractions.Plugins;
using Org.BouncyCastle.Math.EC.Rfc8032;

namespace Mokaterm.Core.Plugins.Gallery;

/// <summary>
/// The Ed25519 signature over <c>index.json</c>: 64 bytes, written as one line of base64 in <c>index.json.sig</c>. It is
/// checked against the exact bytes that were downloaded before a single field of them is read, so neither GitHub Pages
/// nor anything between it and the app can change what is listed.
/// </summary>
internal static class PluginIndexSignature
{
	/// <summary>The largest signature file read; a real one is 88 characters and a newline.</summary>
	public const int MaxFileBytes = 1024;

	private const int Base64Length = 88;

	/// <summary>Decodes the trusted keys. A key that is not 32 bytes of base64 is the host's mistake, so it throws.</summary>
	/// <exception cref="ArgumentException">A key is not 32 bytes of base64.</exception>
	public static byte[][] ReadKeys(IReadOnlyList<string> keys)
	{
		ArgumentNullException.ThrowIfNull(keys);
		if (keys.Count == 0)
		{
			throw new ArgumentException("The gallery needs at least one trusted key.", nameof(keys));
		}

		byte[][] decoded = new byte[keys.Count][];
		for (int index = 0; index < keys.Count; index++)
		{
			byte[] key = new byte[Ed25519.PublicKeySize];
			if (!Convert.TryFromBase64String(keys[index] ?? "", key, out int written) || written != Ed25519.PublicKeySize)
			{
				throw new ArgumentException($"Trusted gallery key {index + 1} is not 32 bytes of base64.", nameof(keys));
			}

			decoded[index] = key;
		}

		return decoded;
	}

	/// <summary>Reads the signature out of <c>index.json.sig</c>.</summary>
	/// <exception cref="PluginGalleryException">The file holds no signature.</exception>
	public static byte[] ReadFile(ReadOnlySpan<byte> file)
	{
		// One line of base64. A final newline is allowed, and so is the CR a Windows checkout of the gallery would add.
		ReadOnlySpan<byte> text = file;
		if (text.EndsWith("\n"u8))
		{
			text = text[..^1];
		}

		if (text.EndsWith("\r"u8))
		{
			text = text[..^1];
		}

		byte[] signature = new byte[Ed25519.SignatureSize];
		if (text.Length != Base64Length
			|| Base64.DecodeFromUtf8(text, signature, out _, out int written) != OperationStatus.Done
			|| written != Ed25519.SignatureSize)
		{
			throw new PluginGalleryException("The plugin list's signature file holds no signature, so the list was not used.");
		}

		return signature;
	}

	/// <summary>True when <paramref name="signature"/> over <paramref name="index"/> was made with one of <paramref name="keys"/>.</summary>
	public static bool Verify(byte[] index, byte[] signature, IEnumerable<byte[]> keys)
	{
		ArgumentNullException.ThrowIfNull(index);
		ArgumentNullException.ThrowIfNull(signature);
		ArgumentNullException.ThrowIfNull(keys);
		return signature.Length == Ed25519.SignatureSize
			&& keys.Any(key => key.Length == Ed25519.PublicKeySize && Ed25519.Verify(signature, 0, key, 0, index, 0, index.Length));
	}
}
