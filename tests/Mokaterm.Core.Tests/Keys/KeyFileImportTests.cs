using System.Security.Cryptography;
using System.Text;
using Mokaterm.Abstractions.Import;
using Mokaterm.Core.Keys;

namespace Mokaterm.Core.Tests.Keys;

/// <summary>What the import makes of each kind of key file a login can name.</summary>
public sealed class KeyFileImportTests
{
	private const string Passphrase = "correct horse";

	[Fact]
	public void Read_KeepsAnOpenSshKeyAsItIs()
	{
		using KeyFileMaterial material = KeyFileImport.Read(Bytes(ReferenceKeys.Ed25519));

		Assert.Equal(ImportedKeyStatus.Ready, material.Status);
		Assert.Equal(ReferenceKeys.Ed25519.Trim(), material.KeyText!.RevealString());
		Assert.Equal("ssh-ed25519", material.Algorithm);
		Assert.Equal(ReferenceKeys.Ed25519Fingerprint, material.Fingerprint);
		Assert.False(material.NeedsPassphrase);
	}

	[Fact]
	public void Read_KeepsAProtectedOpenSshKeyLocked_WithTheFingerprintOfItsPublicHalf()
	{
		using KeyFileMaterial material = KeyFileImport.Read(Bytes(ReferenceKeys.EncryptedEd25519));

		Assert.Equal(ImportedKeyStatus.Ready, material.Status);
		Assert.True(material.NeedsPassphrase);
		Assert.Equal(ReferenceKeys.EncryptedEd25519.Trim(), material.KeyText!.RevealString());
		Assert.Equal(new SshKeyGenerator().ReadPublicKey(ReferenceKeys.EncryptedEd25519)!.Fingerprint, material.Fingerprint);
	}

	[Theory]
	[InlineData(SecurityKeyVectors.EcdsaSk, SecurityKeyVectors.EcdsaSkFingerprint, false)]
	[InlineData(SecurityKeyVectors.Ed25519SkEncrypted, SecurityKeyVectors.Ed25519SkFingerprint, true)]
	public void Read_TakesSecurityKeyFiles(string text, string fingerprint, bool locked)
	{
		using KeyFileMaterial material = KeyFileImport.Read(Bytes(text));

		Assert.Equal(ImportedKeyStatus.Ready, material.Status);
		Assert.Equal(fingerprint, material.Fingerprint);
		Assert.Equal(locked, material.NeedsPassphrase);
	}

	[Fact]
	public void Read_ConvertsAnOpenPuttyKey_AndKeepsAProtectedOneAsItIs()
	{
		TestKeyPair key = TestKeyPair.Rsa();
		string open = PuttyKeyWriter.Write(3, key, "work", null);
		string locked = PuttyKeyWriter.Write(2, key, "work", Passphrase);

		using KeyFileMaterial converted = KeyFileImport.Read(Bytes(open));
		using KeyFileMaterial kept = KeyFileImport.Read(Bytes(locked));

		Assert.Equal(ImportedKeyStatus.Ready, converted.Status);
		Assert.StartsWith(OpenSshPrivateKeyFile.BeginMarker, converted.KeyText!.RevealString(), StringComparison.Ordinal);
		Assert.False(converted.NeedsPassphrase);
		Assert.Equal(ImportedKeyStatus.Ready, kept.Status);
		Assert.Equal(locked.Trim(), kept.KeyText!.RevealString());
		Assert.True(kept.NeedsPassphrase);
		Assert.Equal(key.Fingerprint, converted.Fingerprint);
		Assert.Equal(key.Fingerprint, kept.Fingerprint);
	}

	[Fact]
	public void Read_RefusesAPuttyKeyWhoseMacDoesNotMatch()
	{
		string text = PuttyKeyWriter.Write(3, TestKeyPair.Ed25519Key(), "work", null, tamper: blob => blob[5] ^= 0x40);

		using KeyFileMaterial material = KeyFileImport.Read(Bytes(text));

		Assert.Equal(ImportedKeyStatus.Unreadable, material.Status);
		Assert.Null(material.KeyText);
		Assert.Contains("MAC", material.Reason, StringComparison.Ordinal);
	}

	[Fact]
	public void Read_ConvertsAnOpenRsaPemKey()
	{
		using RSA rsa = RSA.Create(2048);
		string pem = rsa.ExportRSAPrivateKeyPem();

		using KeyFileMaterial material = KeyFileImport.Read(Bytes(pem));

		Assert.Equal(ImportedKeyStatus.Ready, material.Status);
		Assert.Equal("ssh-rsa", material.Algorithm);
		string openSsh = material.KeyText!.RevealString();
		Assert.True(OpenSshPrivateKeyFile.TryReadPublicPart(openSsh, out byte[] blob, out _));
		Assert.Equal(material.Fingerprint, new SshKeyGenerator().ReadPublicKey(openSsh)!.Fingerprint);
		RSAParameters parameters = rsa.ExportParameters(false);
		Assert.Equal(PuttyKeyWriter.String("ssh-rsa"u8).Concat(PuttyKeyWriter.MpInt(parameters.Exponent)).Concat(PuttyKeyWriter.MpInt(parameters.Modulus)), blob);
	}

	[Fact]
	public void Read_KeepsAProtectedRsaPemKeyLocked_WithNoFingerprint()
	{
		const string Pem = """
			-----BEGIN RSA PRIVATE KEY-----
			Proc-Type: 4,ENCRYPTED
			DEK-Info: AES-128-CBC,0123456789ABCDEF0123456789ABCDEF

			c2VjcmV0IHN0dWZmIHRoYXQgaXMgbm90IGRlY3J5cHRlZCBoZXJlLi4uLi4uLi4u
			-----END RSA PRIVATE KEY-----
			""";

		using KeyFileMaterial material = KeyFileImport.Read(Bytes(Pem));

		Assert.Equal(ImportedKeyStatus.Ready, material.Status);
		Assert.True(material.NeedsPassphrase);
		Assert.Null(material.Fingerprint);
		Assert.Equal(Pem, material.KeyText!.RevealString());
	}

	[Fact]
	public void Read_RefusesKeyTypesTheSshModuleDoesNotSignWith()
	{
		byte[] dsaPublic = [.. PuttyKeyWriter.String("ssh-dss"u8), .. PuttyKeyWriter.MpInt([7])];
		string openSsh = OpenSshPrivateKeyFile.Write(dsaPublic, [.. PuttyKeyWriter.String("ssh-dss"u8), .. PuttyKeyWriter.MpInt([7])], "", default);
		string putty = PuttyKeyWriter.Write(2, new TestKeyPair("ssh-dss", dsaPublic, PuttyKeyWriter.MpInt([3])), "dsa", null);

		using KeyFileMaterial fromOpenSsh = KeyFileImport.Read(Bytes(openSsh));
		using KeyFileMaterial fromPutty = KeyFileImport.Read(Bytes(putty));

		Assert.Equal(ImportedKeyStatus.Unsupported, fromOpenSsh.Status);
		Assert.Equal(ImportedKeyStatus.Unsupported, fromPutty.Status);
		Assert.Contains("DSA", fromPutty.Reason, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("-----BEGIN PRIVATE KEY-----\nMIIB\n-----END PRIVATE KEY-----", ImportedKeyStatus.Unsupported)]
	[InlineData("-----BEGIN EC PRIVATE KEY-----\nMHcC\n-----END EC PRIVATE KEY-----", ImportedKeyStatus.Unsupported)]
	[InlineData("ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIEpm2W1S7V34Go0Oa/CuzMQbwvV0GaD0rY+bs50gIGHV me", ImportedKeyStatus.Unsupported)]
	[InlineData("Host *\n  User root", ImportedKeyStatus.Unreadable)]
	[InlineData("   \r\n", ImportedKeyStatus.Unreadable)]
	[InlineData("-----BEGIN OPENSSH PRIVATE KEY-----\nbm90IGEga2V5\n-----END OPENSSH PRIVATE KEY-----", ImportedKeyStatus.Unreadable)]
	[InlineData("-----BEGIN RSA PRIVATE KEY-----\nbm90IGEga2V5\n-----END RSA PRIVATE KEY-----", ImportedKeyStatus.Unreadable)]
	public void Read_SaysWhyAFileIsNotUsable(string text, ImportedKeyStatus expected)
	{
		using KeyFileMaterial material = KeyFileImport.Read(Bytes(text));

		Assert.Equal(expected, material.Status);
		Assert.Null(material.KeyText);
		Assert.False(string.IsNullOrWhiteSpace(material.Reason));
	}

	[Fact]
	public void Read_DropsAByteOrderMarkBeforeKeepingTheKey()
	{
		using KeyFileMaterial material = KeyFileImport.Read([0xEF, 0xBB, 0xBF, .. Bytes(ReferenceKeys.Ed25519)]);

		Assert.Equal(ImportedKeyStatus.Ready, material.Status);
		Assert.StartsWith(OpenSshPrivateKeyFile.BeginMarker, material.KeyText!.RevealString(), StringComparison.Ordinal);
	}

	[Fact]
	public void Read_RefusesAFileFarTooLargeToBeAKey()
	{
		byte[] text = Bytes(OpenSshPrivateKeyFile.BeginMarker + new string('A', KeyFileImport.MaxFileBytes) + OpenSshPrivateKeyFile.EndMarker);

		using KeyFileMaterial material = KeyFileImport.Read(text);

		Assert.Equal(ImportedKeyStatus.Unreadable, material.Status);
	}

	private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);
}
