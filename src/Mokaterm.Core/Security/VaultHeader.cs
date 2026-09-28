namespace Mokaterm.Core.Security;

/// <summary>
/// The plain <c>vault.json</c> file: key derivation parameters, the data key wrapped under the password key,
/// a key check for device-unwrapped keys and the optional device-protected copy. Byte arrays serialize as base64.
/// </summary>
internal sealed record VaultHeader
{
	public const int CurrentFormat = 1;

	public required int Format { get; init; }

	public required Guid VaultId { get; init; }

	public required VaultKdfParameters Kdf { get; init; }

	public required VaultWrappedKey MasterKey { get; init; }

	/// <summary>HMAC-SHA256 of <see cref="VaultCrypto.KeyCheckLabel"/> under the data key.</summary>
	public required byte[] KeyCheck { get; init; }

	public VaultDeviceKey? Device { get; init; }

	public DateTimeOffset CreatedAt { get; init; }

	public DateTimeOffset UpdatedAt { get; init; }

	/// <summary>True when every field has the shape this version writes and parameters are within sane limits.</summary>
	public bool IsWellFormed() =>
		Format == CurrentFormat
		&& VaultId != Guid.Empty
		&& Kdf is not null && Kdf.IsWellFormed()
		&& MasterKey is not null && MasterKey.IsWellFormed()
		&& KeyCheck is { Length: VaultCrypto.KeyCheckSize }
		&& (Device is null || Device.ProtectedKey is { Length: > 0 });
}

internal sealed record VaultKdfParameters
{
	public const string Argon2id = "argon2id";
	public const string Pbkdf2Sha256 = "pbkdf2-sha256";
	public const int SaltSize = 16;

	// Upper bounds keep a damaged or hostile header from exhausting memory or CPU during unlock.
	private const int MaxArgon2MemoryKiB = 1024 * 1024;
	private const int MaxArgon2Iterations = 64;
	private const int MaxArgon2Parallelism = 64;
	private const int MaxPbkdf2Iterations = 10_000_000;

	public required string Algorithm { get; init; }

	public int MemoryKiB { get; init; }

	public required int Iterations { get; init; }

	public int Parallelism { get; init; }

	public required byte[] Salt { get; init; }

	public bool IsWellFormed() =>
		Salt is { Length: SaltSize }
		&& Algorithm switch
		{
			Argon2id => Iterations is >= 1 and <= MaxArgon2Iterations
				&& Parallelism is >= 1 and <= MaxArgon2Parallelism
				&& MemoryKiB >= 8 * Parallelism && MemoryKiB <= MaxArgon2MemoryKiB,
			Pbkdf2Sha256 => Iterations is >= 1 and <= MaxPbkdf2Iterations,
			_ => false,
		};
}

/// <summary>A 32-byte key encrypted with AES-256-GCM.</summary>
internal sealed record VaultWrappedKey
{
	public required byte[] Nonce { get; init; }

	public required byte[] Tag { get; init; }

	public required byte[] Ciphertext { get; init; }

	public bool IsWellFormed() =>
		Nonce is { Length: AesGcmBox.NonceSize }
		&& Tag is { Length: AesGcmBox.TagSize }
		&& Ciphertext is { Length: AesGcmBox.KeySize };
}

/// <summary>The data key protected by <see cref="Abstractions.Security.IDeviceKeyProtector"/>.</summary>
internal sealed record VaultDeviceKey
{
	public required byte[] ProtectedKey { get; init; }
}
