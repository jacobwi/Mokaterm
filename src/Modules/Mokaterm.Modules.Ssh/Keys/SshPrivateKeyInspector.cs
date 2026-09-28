using System.Security.Cryptography;
using System.Text;
using Mokaterm.Abstractions.Credentials;
using Renci.SshNet;

namespace Mokaterm.Modules.Ssh.Keys;

/// <summary>Validates private keys for the credential editor and the keychain with SSH.NET.</summary>
internal sealed class SshPrivateKeyInspector : IPrivateKeyInspector
{
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
}
