using System.Security.Cryptography;
using System.Text;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Modules.Ssh.Connection;
using Renci.SshNet;

namespace Mokaterm.Modules.Ssh.Keys;

/// <summary>
/// Validates private keys for the credential editor and the keychain: with SSH.NET, and with Core's reader for security
/// key files, which SSH.NET cannot load.
/// </summary>
internal sealed class SshPrivateKeyInspector : IPrivateKeyInspector
{
	private readonly ISshSecurityKeyReader? _securityKeys;

	public SshPrivateKeyInspector(ISshSecurityKeyReader? securityKeys = null) => _securityKeys = securityKeys;

	public PrivateKeyInspection Inspect(string privateKey, string? passphrase)
	{
		ArgumentNullException.ThrowIfNull(privateKey);
		if (privateKey.Length > SshPrivateKeys.MaxKeyBytes)
		{
			return new PrivateKeyInspection(PrivateKeyStatus.Invalid, Error: "This file is too large to be a private key.");
		}

		byte[] text = GC.AllocateArray<byte>(Encoding.UTF8.GetByteCount(privateKey), pinned: true);
		try
		{
			Encoding.UTF8.GetBytes(privateKey, text);
			if (SshPrivateKeys.IsSecurityKey(text))
			{
				return InspectSecurityKey(text, passphrase);
			}

			PrivateKeyStatus status = SshPrivateKeys.TryLoad(text, passphrase, out PrivateKeyFile? keyFile, out string? error);
			if (keyFile is null)
			{
				return new PrivateKeyInspection(status, Error: error);
			}

			using (keyFile)
			{
				return new PrivateKeyInspection(PrivateKeyStatus.Valid, SshPrivateKeys.AlgorithmOf(keyFile), SshPrivateKeys.FingerprintOf(keyFile));
			}
		}
		finally
		{
			CryptographicOperations.ZeroMemory(text);
		}
	}

	private PrivateKeyInspection InspectSecurityKey(ReadOnlySpan<byte> text, string? passphrase)
	{
		if (_securityKeys is null)
		{
			return new PrivateKeyInspection(PrivateKeyStatus.Unsupported, Error: "Security key files cannot be read here.");
		}

		SecurityKeyFileResult result = _securityKeys.Read(text, passphrase);
		if (!result.IsValid)
		{
			return new PrivateKeyInspection(result.Status, Error: result.Error);
		}

		CryptographicOperations.ZeroMemory(result.Key.KeyHandle);
		return new PrivateKeyInspection(PrivateKeyStatus.Valid, result.Key.KeyType, SshHostKeys.Fingerprint(result.Key.PublicKeyBlob));
	}
}
