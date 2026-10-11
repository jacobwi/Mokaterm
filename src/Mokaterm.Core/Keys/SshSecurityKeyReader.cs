using Mokaterm.Abstractions.Credentials;
using Mokaterm.Core.Security;

namespace Mokaterm.Core.Keys;

/// <summary>Opens OpenSSH security key files for the SSH module and the keychain, which cannot read the format themselves.</summary>
internal sealed class SshSecurityKeyReader : ISshSecurityKeyReader
{
	public SecurityKeyFileResult Read(ReadOnlySpan<byte> privateKeyText, string? passphrase)
	{
		PrivateKeyStatus status = OpenSshPrivateKeyFile.TryOpen(privateKeyText, passphrase, out byte[] publicBlob, out PinnedBytes? section, out string? error);
		if (section is null)
		{
			return SecurityKeyFileResult.Failed(status, error ?? "The private key is damaged or incomplete.");
		}

		using (section)
		{
			return SecurityKeyFile.Parse(publicBlob, section.Span);
		}
	}
}
