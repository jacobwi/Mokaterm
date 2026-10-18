using System.Text;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Core.Keys;
using Mokaterm.Core.Security;

namespace Mokaterm.Core.Tests.TestSupport;

/// <summary>
/// Inspects keys the way the SSH module's inspector does for the formats an import stores, built on Core's own readers,
/// since Core's tests cannot reach the module: a protected OpenSSH, PuTTY or PEM key asks for its passphrase, and an
/// open one is valid with the fingerprint OpenSSH prints.
/// </summary>
internal sealed class CoreKeyInspector : IPrivateKeyInspector
{
	public int Calls { get; private set; }

	public PrivateKeyInspection Inspect(string privateKey, string? passphrase)
	{
		Calls++;
		byte[] text = Encoding.UTF8.GetBytes(privateKey);
		if (PuttyKeyFile.LooksLikeOne(text))
		{
			PrivateKeyStatus status = PuttyKeyFile.TryConvert(text, passphrase, out string? converted, out byte[] blob, out string? error);
			return converted is null ? new PrivateKeyInspection(status, Error: error) : Valid(blob);
		}

		if (privateKey.Contains("Proc-Type: 4,ENCRYPTED", StringComparison.Ordinal))
		{
			return passphrase is null
				? new PrivateKeyInspection(PrivateKeyStatus.PassphraseRequired)
				: new PrivateKeyInspection(PrivateKeyStatus.Unsupported, Error: "Encrypted PEM is opened by the SSH module only.");
		}

		if (!OpenSshPrivateKeyFile.TryReadHeader(text, out byte[] publicBlob, out bool encrypted))
		{
			return new PrivateKeyInspection(PrivateKeyStatus.Invalid);
		}

		if (encrypted && passphrase is null)
		{
			return new PrivateKeyInspection(PrivateKeyStatus.PassphraseRequired);
		}

		PrivateKeyStatus opened = OpenSshPrivateKeyFile.TryOpen(text, passphrase, out _, out PinnedBytes? section, out string? reason);
		section?.Dispose();
		return opened == PrivateKeyStatus.Valid ? Valid(publicBlob) : new PrivateKeyInspection(opened, Error: reason);
	}

	private static PrivateKeyInspection Valid(byte[] publicBlob)
	{
		SshPublicKey key = SshKeyGenerator.Describe(publicBlob, "");
		return new PrivateKeyInspection(PrivateKeyStatus.Valid, key.Algorithm, key.Fingerprint);
	}
}
