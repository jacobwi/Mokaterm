using System.Diagnostics.CodeAnalysis;

namespace Mokaterm.Abstractions.Credentials;

/// <summary>
/// The OpenSSH names for security key types and the rule for the application string they are bound to. Core writes
/// key files with these, the SSH module signs with them and the keychain shows them, so they live here once.
/// </summary>
public static class SecurityKeyTypes
{
	public const string EcdsaKeyType = "sk-ecdsa-sha2-nistp256@openssh.com";

	public const string Ed25519KeyType = "sk-ssh-ed25519@openssh.com";

	/// <summary>
	/// The signature type for an ECDSA security key whose signature covers a WebAuthn client data JSON instead of the
	/// message hash. The key type stays <see cref="EcdsaKeyType"/>; only the signature carries this name.
	/// </summary>
	public const string WebAuthnEcdsaSignatureType = "webauthn-sk-ecdsa-sha2-nistp256@openssh.com";

	/// <summary>What <c>ssh-keygen</c> binds a key to when it is not told otherwise.</summary>
	public const string DefaultApplication = "ssh:";

	/// <summary><c>ssh-keygen</c> refuses an application that does not start with this.</summary>
	public const string ApplicationPrefix = "ssh:";

	public const int MaxApplicationLength = 128;

	/// <summary>
	/// True for every key type that lives on a security key, certificates included. Such a key cannot sign without
	/// the device, so a list shows it apart from ordinary keys.
	/// </summary>
	public static bool IsSecurityKey([NotNullWhen(true)] string? keyType) =>
		keyType is not null && keyType.StartsWith("sk-", StringComparison.Ordinal);

	public static string KeyType(SecurityKeyAlgorithm algorithm) => algorithm switch
	{
		SecurityKeyAlgorithm.EcdsaP256 => EcdsaKeyType,
		SecurityKeyAlgorithm.Ed25519 => Ed25519KeyType,
		_ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Unknown security key algorithm."),
	};

	/// <summary>The algorithm of a plain (not certificate) security key type.</summary>
	public static bool TryGetAlgorithm(string? keyType, out SecurityKeyAlgorithm algorithm)
	{
		switch (keyType)
		{
			case EcdsaKeyType:
				algorithm = SecurityKeyAlgorithm.EcdsaP256;
				return true;
			case Ed25519KeyType:
				algorithm = SecurityKeyAlgorithm.Ed25519;
				return true;
			default:
				algorithm = default;
				return false;
		}
	}

	/// <summary>
	/// Why <paramref name="application"/> cannot name a new key, or null when it can. Printable ASCII only: the string is
	/// the relying party id the authenticator hashes, and a quote or backslash could not be written into the WebAuthn
	/// client data that ECDSA keys may have to sign.
	/// </summary>
	public static string? ValidateApplication(string? application)
	{
		if (string.IsNullOrEmpty(application))
		{
			return "Enter an application, such as ssh:";
		}

		if (!application.StartsWith(ApplicationPrefix, StringComparison.Ordinal))
		{
			return "The application has to start with ssh:";
		}

		if (application.Length > MaxApplicationLength)
		{
			return $"The application can be at most {MaxApplicationLength} characters long.";
		}

		foreach (char character in application)
		{
			if (character is < '!' or > '~' or '"' or '\\')
			{
				return "Use letters, digits and punctuation only, without spaces, quotes or backslashes.";
			}
		}

		return null;
	}
}
