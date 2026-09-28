using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Interaction;

namespace Mokaterm.Core.Tests.Abstractions;

/// <summary>
/// A record's generated ToString prints every property, so a log line or exception message that formats one of these
/// would otherwise carry the secret with it.
/// </summary>
public sealed class SecretRecordFormattingTests
{
	private const string Password = "hunter2-password";
	private const string ProxyPassword = "proxy-hunter2-password";
	private const string Passphrase = "correct-horse-passphrase";
	private const string PrivateKey = "-----BEGIN OPENSSH PRIVATE KEY-----\nb3BlbnNzaC1rZXktdjEAAAAA\n-----END OPENSSH PRIVATE KEY-----\n";

	[Fact]
	public void CredentialSecretInput_ToString_SaysWhichFieldsAreSetButNotWhatTheyHold()
	{
		string text = new CredentialSecretInput { Password = Password, PrivateKey = PrivateKey, Passphrase = "", ProxyPassword = ProxyPassword }.ToString();

		Assert.DoesNotContain(Password, text, StringComparison.Ordinal);
		Assert.DoesNotContain(ProxyPassword, text, StringComparison.Ordinal);
		Assert.DoesNotContain("OPENSSH", text, StringComparison.Ordinal);
		Assert.Contains("Password = (set)", text, StringComparison.Ordinal);
		Assert.Contains("Passphrase = (empty)", text, StringComparison.Ordinal);
		Assert.Contains("ProxyPassword = (set)", text, StringComparison.Ordinal);
		Assert.Contains("(set)", $"{new CredentialSecretInput { PrivateKey = PrivateKey }}", StringComparison.Ordinal);
		Assert.Contains("Password = null", new CredentialSecretInput().ToString(), StringComparison.Ordinal);
	}

	[Fact]
	public void SshKeyRequest_ToString_KeepsTheTypeAndCommentButNotThePassphrase()
	{
		string text = new SshKeyRequest { Type = SshKeyType.Ed25519, Comment = "ops@build", Passphrase = Passphrase }.ToString();

		Assert.DoesNotContain(Passphrase, text, StringComparison.Ordinal);
		Assert.Contains("Ed25519", text, StringComparison.Ordinal);
		Assert.Contains("ops@build", text, StringComparison.Ordinal);
	}

	[Fact]
	public void CredentialPromptResult_ToString_KeepsTheNameButNotThePassword()
	{
		string text = new CredentialPromptResult("ops", Password, Save: true).ToString();

		Assert.DoesNotContain(Password, text, StringComparison.Ordinal);
		Assert.Contains("ops", text, StringComparison.Ordinal);
		Assert.Contains("Password = (set)", text, StringComparison.Ordinal);
		Assert.Contains("Save = True", text, StringComparison.Ordinal);
		Assert.Contains("Password = (empty)", new CredentialPromptResult("ops", "", Save: false).ToString(), StringComparison.Ordinal);
	}

	[Fact]
	public void SecretPromptResult_ToString_SaysASecretWasGivenButNotWhich()
	{
		string text = new SecretPromptResult(Passphrase, Remember: true).ToString();

		Assert.DoesNotContain(Passphrase, text, StringComparison.Ordinal);
		Assert.Contains("Secret = (set)", text, StringComparison.Ordinal);
		Assert.Contains("Remember = True", text, StringComparison.Ordinal);
	}

	[Fact]
	public void GeneratedSshKey_ToString_ShowsThePublicHalfOnly()
	{
		GeneratedSshKey key = new()
		{
			PrivateKey = PrivateKey,
			PublicKey = new SshPublicKey("ssh-ed25519", "ops@build", "ssh-ed25519 AAAAC3Nz ops@build", "SHA256:abc"),
			HasPassphrase = true,
			SuggestedFileName = "id_ed25519",
		};

		string text = key.ToString();

		Assert.DoesNotContain("PRIVATE KEY", text, StringComparison.Ordinal);
		Assert.DoesNotContain("b3BlbnNzaC1rZXktdjEAAAAA", text, StringComparison.Ordinal);
		Assert.Contains("SHA256:abc", text, StringComparison.Ordinal);
		Assert.Contains("id_ed25519", text, StringComparison.Ordinal);
	}
}
