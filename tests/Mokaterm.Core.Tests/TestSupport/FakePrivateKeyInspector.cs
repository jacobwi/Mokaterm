using System.Security.Cryptography;
using System.Text;
using Mokaterm.Abstractions.Credentials;

namespace Mokaterm.Core.Tests.TestSupport;

/// <summary>
/// Accepts text starting with <see cref="Header"/>. Keys containing <c>ENCRYPTED</c> need the passphrase
/// <see cref="Passphrase"/>.
/// </summary>
public sealed class FakePrivateKeyInspector : IPrivateKeyInspector
{
	public const string Header = "-----BEGIN OPENSSH PRIVATE KEY-----";
	public const string Passphrase = "open sesame";
	public const string Algorithm = "ssh-ed25519";

	public static string PlainKey { get; } = Header + "\nplain-key-body\n-----END OPENSSH PRIVATE KEY-----";

	public static string EncryptedKey { get; } = Header + "\nENCRYPTED-key-body\n-----END OPENSSH PRIVATE KEY-----";

	public int Calls { get; private set; }

	public static string FingerprintOf(string privateKey) =>
		"SHA256:" + Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(privateKey))).TrimEnd('=');

	public PrivateKeyInspection Inspect(string privateKey, string? passphrase)
	{
		Calls++;
		if (!privateKey.StartsWith(Header, StringComparison.Ordinal))
		{
			return new PrivateKeyInspection(PrivateKeyStatus.Invalid);
		}

		if (privateKey.Contains("ENCRYPTED", StringComparison.Ordinal))
		{
			if (passphrase is null)
			{
				return new PrivateKeyInspection(PrivateKeyStatus.PassphraseRequired);
			}

			if (passphrase != Passphrase)
			{
				return new PrivateKeyInspection(PrivateKeyStatus.WrongPassphrase);
			}
		}

		return new PrivateKeyInspection(PrivateKeyStatus.Valid, Algorithm, FingerprintOf(privateKey));
	}
}
