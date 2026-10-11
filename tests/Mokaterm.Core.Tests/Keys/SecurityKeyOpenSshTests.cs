using Mokaterm.Abstractions.Credentials;
using Mokaterm.Core.Keys;

namespace Mokaterm.Core.Tests.Keys;

/// <summary>
/// Security key files this app writes, read by the machine's own <c>ssh-keygen</c>. OpenSSH parses the private section
/// strictly (field order, padding, the public half matching the private one), so a file it reads back to the same line
/// is a file <c>ssh</c> can use. Skipped where no <c>ssh-keygen</c> is installed.
/// </summary>
public sealed class SecurityKeyOpenSshTests : IDisposable
{
	private readonly SoftwareSecurityKey _device = new();

	public void Dispose() => _device.Dispose();

	[Theory(Timeout = TestTimeouts.NetworkMilliseconds)]
	[InlineData(SshKeyType.EcdsaSecurityKey, "")]
	[InlineData(SshKeyType.EcdsaSecurityKey, "pass phrase")]
	[InlineData(SshKeyType.Ed25519SecurityKey, "")]
	[InlineData(SshKeyType.Ed25519SecurityKey, "pass phrase")]
	public async Task SshKeygen_ReadsAGeneratedSecurityKeyFile(SshKeyType type, string passphrase)
	{
		CancellationToken ct = TestContext.Current.CancellationToken;
		string keygen = OpenSshKeygen.Find() ?? "";
		Assert.SkipWhen(keygen.Length == 0, "No ssh-keygen is installed here to check the file against.");
		GeneratedSshKey key = await new SshKeyGenerator(_device).GenerateAsync(
			new SshKeyRequest
			{
				Type = type,
				Comment = "me@box",
				Passphrase = passphrase.Length == 0 ? null : passphrase,
				ResidentKey = true,
				RequireUserVerification = true,
				Application = "ssh:mokaterm",
			},
			ct);

		await using TempFolder folder = new();
		string path = Path.Combine(folder.Path, key.SuggestedFileName);
		await File.WriteAllTextAsync(path, key.PrivateKey, ct);

		(int exitCode, string output, string error) = await OpenSshKeygen.RunAsync(keygen, ["-y", "-P", passphrase, "-f", path], null, ct);
		Assert.True(exitCode == 0, error);
		Assert.Equal(key.PublicKey.AuthorizedKeysLine, output.Trim());

		(exitCode, output, error) = await OpenSshKeygen.RunAsync(keygen, ["-l", "-f", path], null, ct);
		Assert.True(exitCode == 0, error);
		Assert.Contains(key.PublicKey.Fingerprint, output, StringComparison.Ordinal);
	}
}
