using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Mokaterm.Core.Security;

/// <summary>Vault-specific constructions on top of <see cref="AesGcmBox"/>: associated data, key wrapping and the key check.</summary>
internal static class VaultCrypto
{
	public const int KeyCheckSize = 32;

	public const string KeyCheckLabel = "mokaterm:key-check:v1";

	private static readonly byte[] KeyCheckLabelBytes = Encoding.UTF8.GetBytes(KeyCheckLabel);

	public static byte[] MasterKeyAssociatedData(Guid vaultId) =>
		Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"mokaterm:vault:{vaultId:D}:master-key:v1"));

	public static byte[] DocumentAssociatedData(Guid vaultId, string documentName) =>
		Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"mokaterm:vault:{vaultId:D}:doc:{documentName}:v1"));

	public static byte[] CredentialFieldAssociatedData(Guid credentialId, string field) =>
		Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"mokaterm:credential:{credentialId:D}:{field}:v1"));

	public static PinnedBytes CreateDataKey()
	{
		PinnedBytes key = new(AesGcmBox.KeySize);
		RandomNumberGenerator.Fill(key.Span);
		return key;
	}

	public static byte[] ComputeKeyCheck(ReadOnlySpan<byte> dataKey) =>
		HMACSHA256.HashData(dataKey, KeyCheckLabelBytes);

	/// <summary>Compares in constant time so a device-unwrapped key cannot be probed byte by byte.</summary>
	public static bool VerifyKeyCheck(ReadOnlySpan<byte> dataKey, ReadOnlySpan<byte> expected)
	{
		if (dataKey.Length != AesGcmBox.KeySize)
		{
			return false;
		}

		Span<byte> actual = stackalloc byte[KeyCheckSize];
		HMACSHA256.HashData(dataKey, KeyCheckLabelBytes, actual);
		return CryptographicOperations.FixedTimeEquals(actual, expected);
	}

	public static VaultWrappedKey WrapKey(ReadOnlySpan<byte> wrappingKey, ReadOnlySpan<byte> dataKey, Guid vaultId)
	{
		byte[] nonce = RandomNumberGenerator.GetBytes(AesGcmBox.NonceSize);
		byte[] tag = new byte[AesGcmBox.TagSize];
		byte[] ciphertext = new byte[dataKey.Length];
		AesGcmBox.Encrypt(wrappingKey, nonce, dataKey, MasterKeyAssociatedData(vaultId), tag, ciphertext);
		return new VaultWrappedKey { Nonce = nonce, Tag = tag, Ciphertext = ciphertext };
	}

	/// <summary>Unwraps the data key, or returns null when the tag does not verify (a wrong password).</summary>
	public static PinnedBytes? TryUnwrapKey(ReadOnlySpan<byte> wrappingKey, VaultWrappedKey wrapped, Guid vaultId)
	{
		PinnedBytes dataKey = new(wrapped.Ciphertext.Length);
		try
		{
			AesGcmBox.Decrypt(wrappingKey, wrapped.Nonce, wrapped.Tag, wrapped.Ciphertext, MasterKeyAssociatedData(vaultId), dataKey.Span);
			return dataKey;
		}
		catch (AuthenticationTagMismatchException)
		{
			dataKey.Dispose();
			return null;
		}
		catch
		{
			dataKey.Dispose();
			throw;
		}
	}
}
