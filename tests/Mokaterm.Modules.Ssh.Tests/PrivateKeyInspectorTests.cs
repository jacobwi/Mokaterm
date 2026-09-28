using Mokaterm.Abstractions.Credentials;
using Mokaterm.Modules.Ssh.Keys;

namespace Mokaterm.Modules.Ssh.Tests;

public sealed class PrivateKeyInspectorTests
{
	private readonly SshPrivateKeyInspector _inspector = new();

	public static TheoryData<string, string?, string, string> ValidKeys => new()
	{
		{ TestKeys.Ed25519, null, "ssh-ed25519", TestKeys.Ed25519Fingerprint },
		{ TestKeys.EncryptedEd25519, TestKeys.EncryptedEd25519Passphrase, "ssh-ed25519", TestKeys.EncryptedEd25519Fingerprint },
		{ TestKeys.EcdsaP256, null, "ecdsa-sha2-nistp256", TestKeys.EcdsaP256Fingerprint },
		{ TestKeys.EncryptedRsaPem, TestKeys.EncryptedRsaPemPassphrase, "ssh-rsa", TestKeys.EncryptedRsaPemFingerprint },
		{ TestKeys.RsaPkcs8, null, "ssh-rsa", TestKeys.RsaPkcs8Fingerprint },
	};

	[Theory]
	[MemberData(nameof(ValidKeys))]
	public void Inspect_ValidKey_ReportsAlgorithmAndOpenSshFingerprint(string key, string? passphrase, string algorithm, string fingerprint)
	{
		PrivateKeyInspection inspection = _inspector.Inspect(key, passphrase);

		Assert.Equal(PrivateKeyStatus.Valid, inspection.Status);
		Assert.Equal(algorithm, inspection.Algorithm);
		Assert.Equal(fingerprint, inspection.Fingerprint);
		Assert.Null(inspection.Error);
	}

	[Theory]
	[InlineData(TestKeys.EncryptedEd25519)]
	[InlineData(TestKeys.EncryptedRsaPem)]
	public void Inspect_EncryptedKeyWithoutPassphrase_AsksForOne(string key)
	{
		Assert.Equal(PrivateKeyStatus.PassphraseRequired, _inspector.Inspect(key, null).Status);
		Assert.Equal(PrivateKeyStatus.PassphraseRequired, _inspector.Inspect(key, "").Status);
	}

	[Theory]
	[InlineData(TestKeys.EncryptedEd25519)]
	[InlineData(TestKeys.EncryptedRsaPem)]
	public void Inspect_WrongPassphrase_SaysSo(string key)
	{
		PrivateKeyInspection inspection = _inspector.Inspect(key, "not the passphrase");

		Assert.Equal(PrivateKeyStatus.WrongPassphrase, inspection.Status);
		Assert.Null(inspection.Fingerprint);
	}

	[Fact]
	public void Inspect_UnencryptedKeyWithPassphrase_IsStillValid() =>
		Assert.Equal(PrivateKeyStatus.Valid, _inspector.Inspect(TestKeys.Ed25519, "ignored").Status);

	[Fact]
	public void Inspect_ToleratesSurroundingWhitespaceAndWindowsLineEndings()
	{
		string pasted = "\r\n  " + TestKeys.Ed25519.ReplaceLineEndings("\r\n") + "\r\n\r\n";

		PrivateKeyInspection inspection = _inspector.Inspect(pasted, null);

		Assert.Equal(PrivateKeyStatus.Valid, inspection.Status);
		Assert.Equal(TestKeys.Ed25519Fingerprint, inspection.Fingerprint);
	}

	[Fact]
	public void Inspect_DsaKey_IsUnsupported()
	{
		const string Dsa = """
			-----BEGIN DSA PRIVATE KEY-----
			MIIBuwIBAAKBgQDH7m3fT1c2yNvdA8h3kTsqV4vLhJ1Hq9mR2
			-----END DSA PRIVATE KEY-----
			""";

		Assert.Equal(PrivateKeyStatus.Unsupported, _inspector.Inspect(Dsa, null).Status);
	}

	[Theory]
	[InlineData("")]
	[InlineData("not a key at all")]
	[InlineData("-----BEGIN CERTIFICATE-----\nMIIB\n-----END CERTIFICATE-----")]
	[InlineData("-----BEGIN OPENSSH PRIVATE KEY-----\nAAAAB3NzaC1yc2E=\n-----END OPENSSH PRIVATE KEY-----")]
	public void Inspect_Garbage_IsInvalid(string text) =>
		Assert.Equal(PrivateKeyStatus.Invalid, _inspector.Inspect(text, null).Status);

	[Fact]
	public void Inspect_PublicKey_ExplainsTheMistake()
	{
		PrivateKeyInspection inspection = _inspector.Inspect("ssh-ed25519 " + TestKeys.Ed25519PublicKey + " mokaterm-test", null);

		Assert.Equal(PrivateKeyStatus.Invalid, inspection.Status);
		Assert.Contains("public key", inspection.Error, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void Inspect_TruncatedEncryptedOpenSshKey_IsInvalidRatherThanWrongPassphrase()
	{
		string[] lines = TestKeys.EncryptedEd25519.ReplaceLineEndings("\n").Split('\n');
		string truncated = string.Join('\n', lines[..^4]) + "\n" + lines[^1];

		Assert.Equal(PrivateKeyStatus.Invalid, _inspector.Inspect(truncated, TestKeys.EncryptedEd25519Passphrase).Status);
		Assert.Equal(PrivateKeyStatus.Invalid, _inspector.Inspect(truncated, null).Status);
	}

	[Fact]
	public void Inspect_EncryptedPemWithPartialCiphertext_IsInvalid()
	{
		string key = TestKeys.EncryptedRsaPem.ReplaceLineEndings("\n");
		int end = key.IndexOf("\n-----END", StringComparison.Ordinal);
		string damaged = key[..(end - 10)] + key[end..];

		Assert.Equal(PrivateKeyStatus.Invalid, _inspector.Inspect(damaged, TestKeys.EncryptedRsaPemPassphrase).Status);
	}

	[Theory]
	[InlineData(TestKeys.Ed25519, nameof(PrivateKeyFormat.OpenSsh), false)]
	[InlineData(TestKeys.EncryptedEd25519, nameof(PrivateKeyFormat.OpenSsh), true)]
	[InlineData(TestKeys.EncryptedRsaPem, nameof(PrivateKeyFormat.Pem), true)]
	[InlineData(TestKeys.RsaPkcs8, nameof(PrivateKeyFormat.Pkcs8), false)]
	[InlineData("-----BEGIN ENCRYPTED PRIVATE KEY-----\nMIIF\n-----END ENCRYPTED PRIVATE KEY-----", nameof(PrivateKeyFormat.Pkcs8), true)]
	[InlineData("PuTTY-User-Key-File-3: ssh-ed25519\nEncryption: none\n", nameof(PrivateKeyFormat.PuTTY), false)]
	[InlineData("PuTTY-User-Key-File-3: ssh-ed25519\nEncryption: aes256-cbc\n", nameof(PrivateKeyFormat.PuTTY), true)]
	public void Envelope_DetectsFormatAndEncryption(string text, string format, bool encrypted)
	{
		PrivateKeyEnvelope envelope = PrivateKeyEnvelope.Detect(System.Text.Encoding.UTF8.GetBytes(text));

		Assert.Equal(Enum.Parse<PrivateKeyFormat>(format), envelope.Format);
		Assert.Equal(encrypted, envelope.IsEncrypted);
	}
}
