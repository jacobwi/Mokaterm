using System.Security.Cryptography;
using Konscious.Security.Cryptography;

namespace Mokaterm.Core.Security;

/// <summary>Turns the master password into the key that wraps the vault's data key.</summary>
internal static class VaultKeyDerivation
{
	public static VaultKdfParameters CreateArgon2idParameters(VaultOptions options) => new()
	{
		Algorithm = VaultKdfParameters.Argon2id,
		MemoryKiB = options.Argon2MemoryKiB,
		Iterations = options.Argon2Iterations,
		Parallelism = options.Argon2Parallelism,
		Salt = RandomNumberGenerator.GetBytes(VaultKdfParameters.SaltSize),
	};

	/// <summary>Derives a 32-byte key in pinned memory that the caller disposes.</summary>
	/// <exception cref="InvalidDataException">The parameters name an unknown algorithm.</exception>
	public static Task<PinnedBytes> DeriveAsync(string password, VaultKdfParameters kdf, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrEmpty(password);

		// Argon2 takes around a second; the MAUI UI thread must not wait on it.
		return Task.Run(
			async () =>
			{
				using PinnedBytes passwordBytes = PinnedBytes.FromString(password);
				return kdf.Algorithm switch
				{
					VaultKdfParameters.Argon2id => await DeriveArgon2idAsync(passwordBytes, kdf),
					VaultKdfParameters.Pbkdf2Sha256 => DerivePbkdf2(passwordBytes, kdf),
					_ => throw new InvalidDataException($"Unknown key derivation algorithm '{kdf.Algorithm}'."),
				};
			},
			cancellationToken);
	}

	private static async Task<PinnedBytes> DeriveArgon2idAsync(PinnedBytes password, VaultKdfParameters kdf)
	{
		byte[] derived;
		using (Argon2id argon2 = new(password.Array))
		{
			argon2.Salt = kdf.Salt;
			argon2.MemorySize = kdf.MemoryKiB;
			argon2.Iterations = kdf.Iterations;
			argon2.DegreeOfParallelism = kdf.Parallelism;
			derived = await argon2.GetBytesAsync(AesGcmBox.KeySize);
		}

		try
		{
			return PinnedBytes.Copy(derived);
		}
		finally
		{
			CryptographicOperations.ZeroMemory(derived);
		}
	}

	private static PinnedBytes DerivePbkdf2(PinnedBytes password, VaultKdfParameters kdf)
	{
		PinnedBytes key = new(AesGcmBox.KeySize);
		Rfc2898DeriveBytes.Pbkdf2(password.Span, kdf.Salt, key.Span, kdf.Iterations, HashAlgorithmName.SHA256);
		return key;
	}
}
