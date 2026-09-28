using System.Numerics;
using System.Text;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Core.Keys;

namespace Mokaterm.Core.Tests.Keys;

public sealed class SshKeyGeneratorTests
{
	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public void ReadPublicKey_ProducesTheLineOpenSshWroteForTheSameKey()
	{
		SshPublicKey? publicKey = new SshKeyGenerator().ReadPublicKey(ReferenceKeys.Ed25519);

		Assert.NotNull(publicKey);
		Assert.Equal(ReferenceKeys.Ed25519PublicKey, publicKey.AuthorizedKeysLine);
		Assert.Equal(ReferenceKeys.Ed25519Fingerprint, publicKey.Fingerprint);
		Assert.Equal("ssh-ed25519", publicKey.Algorithm);
		Assert.Equal("mokaterm-reference", publicKey.Comment);
	}

	[Fact]
	public void ReadPublicKey_WorksOnAPassphraseProtectedKeyBecauseThePublicHalfIsNotEncrypted()
	{
		SshPublicKey? publicKey = new SshKeyGenerator().ReadPublicKey(ReferenceKeys.EncryptedEd25519);

		Assert.NotNull(publicKey);
		Assert.Equal(ReferenceKeys.EncryptedEd25519PublicKey, publicKey.AuthorizedKeysLine);
		Assert.Equal("", publicKey.Comment);
	}

	[Theory]
	[InlineData("")]
	[InlineData("not a key")]
	[InlineData("-----BEGIN OPENSSH PRIVATE KEY-----\nnot base64 at all !!\n-----END OPENSSH PRIVATE KEY-----")]
	public void ReadPublicKey_ReturnsNullForAnythingThatIsNotAnOpenSshKey(string text) =>
		Assert.Null(new SshKeyGenerator().ReadPublicKey(text));

	[Fact]
	public async Task GenerateAsync_WritesAnEd25519KeyOpenSshCanRead()
	{
		GeneratedSshKey key = await Generate(SshKeyType.Ed25519, "me@box", passphrase: null);

		Assert.Equal("id_ed25519", key.SuggestedFileName);
		Assert.False(key.HasPassphrase);
		Assert.Equal("ssh-ed25519", key.PublicKey.Algorithm);
		Assert.Equal($"ssh-ed25519 {Convert.ToBase64String(Blob(key))} me@box", key.PublicKey.AuthorizedKeysLine);

		ParsedOpenSshKey parsed = OpenSshKeyParser.Parse(key.PrivateKey);
		Assert.Equal("none", parsed.Cipher);
		Assert.Equal("none", parsed.Kdf);
		Assert.Equal(0, parsed.Section.Length % 8);

		(string keyType, List<byte[]> fields, string comment) = OpenSshKeyParser.ReadSection(parsed.Section);
		Assert.Equal("ssh-ed25519", keyType);
		Assert.Equal("me@box", comment);
		Assert.Equal(2, fields.Count);

		byte[] publicPart = PublicKeyBytes(parsed.PublicBlob);
		Assert.Equal(publicPart, fields[0]);

		// OpenSSH stores the 32 byte seed followed by the public key.
		Assert.Equal(64, fields[1].Length);
		Assert.Equal(publicPart, fields[1][32..]);
	}

	[Fact]
	public async Task GenerateAsync_WritesRsaFieldsInOpenSshOrderAndTheyAgreeWithEachOther()
	{
		GeneratedSshKey key = await Generate(SshKeyType.Rsa3072, "rsa@box", passphrase: null);

		Assert.Equal("id_rsa", key.SuggestedFileName);
		Assert.Equal("ssh-rsa", key.PublicKey.Algorithm);

		ParsedOpenSshKey parsed = OpenSshKeyParser.Parse(key.PrivateKey);
		(string keyType, List<byte[]> fields, _) = OpenSshKeyParser.ReadSection(parsed.Section);
		Assert.Equal("ssh-rsa", keyType);
		Assert.Equal(6, fields.Count);

		BigInteger modulus = Number(fields[0]);
		BigInteger exponent = Number(fields[1]);
		BigInteger privateExponent = Number(fields[2]);
		BigInteger inverseQ = Number(fields[3]);
		BigInteger p = Number(fields[4]);
		BigInteger q = Number(fields[5]);

		Assert.Equal(3072, (int)modulus.GetBitLength());
		Assert.Equal(modulus, p * q);
		BigInteger lambda = (p - 1) * (q - 1) / BigInteger.GreatestCommonDivisor(p - 1, q - 1);
		Assert.Equal(BigInteger.One, exponent * privateExponent % lambda);
		Assert.Equal(BigInteger.One, inverseQ * q % p);

		// The public half repeats the same numbers, but as e then n.
		SshWireReader reader = new(parsed.PublicBlob);
		Assert.True(reader.TryReadString(out _));
		Assert.True(reader.TryReadString(out ReadOnlySpan<byte> publicExponent));
		Assert.True(reader.TryReadString(out ReadOnlySpan<byte> publicModulus));
		Assert.Equal(exponent, Number(publicExponent.ToArray()));
		Assert.Equal(modulus, Number(publicModulus.ToArray()));
	}

	[Fact]
	public async Task GenerateAsync_EncryptsThePrivateHalfWithBcryptAndAes256Ctr()
	{
		GeneratedSshKey key = await Generate(SshKeyType.Ed25519, "locked@box", "hunter2");

		Assert.True(key.HasPassphrase);
		ParsedOpenSshKey parsed = OpenSshKeyParser.Parse(key.PrivateKey);
		Assert.Equal("aes256-ctr", parsed.Cipher);
		Assert.Equal("bcrypt", parsed.Kdf);
		Assert.Equal(16, parsed.Salt.Length);
		Assert.Equal(BcryptPbkdf.DefaultRounds, parsed.Rounds);
		Assert.Equal(0, parsed.Section.Length % 16);

		// The check words only match when the passphrase and the KDF are right.
		byte[] section = OpenSshKeyParser.Decrypt(parsed, "hunter2");
		(string keyType, List<byte[]> fields, string comment) = OpenSshKeyParser.ReadSection(section);
		Assert.Equal("ssh-ed25519", keyType);
		Assert.Equal("locked@box", comment);
		Assert.Equal(PublicKeyBytes(parsed.PublicBlob), fields[0]);

		// The public half stays readable without the passphrase.
		SshPublicKey? readBack = new SshKeyGenerator().ReadPublicKey(key.PrivateKey);
		Assert.NotNull(readBack);
		Assert.Equal(key.PublicKey.Fingerprint, readBack.Fingerprint);
	}

	[Fact]
	public async Task GenerateAsync_LeavesOutAnEmptyCommentAndFoldsControlCharactersOut()
	{
		GeneratedSshKey plain = await Generate(SshKeyType.Ed25519, comment: null, passphrase: null);
		Assert.Equal("", plain.PublicKey.Comment);
		Assert.Equal(2, plain.PublicKey.AuthorizedKeysLine.Split(' ').Length);

		GeneratedSshKey messy = await Generate(SshKeyType.Ed25519, "  first\nsecond\t  ", passphrase: null);
		Assert.Equal("first second", messy.PublicKey.Comment);
		Assert.DoesNotContain('\n', messy.PublicKey.AuthorizedKeysLine);
	}

	[Fact]
	public async Task GenerateAsync_GivesEveryKeyItsOwnMaterial()
	{
		GeneratedSshKey first = await Generate(SshKeyType.Ed25519, "a", passphrase: null);
		GeneratedSshKey second = await Generate(SshKeyType.Ed25519, "a", passphrase: null);

		Assert.NotEqual(first.PublicKey.Fingerprint, second.PublicKey.Fingerprint);
	}

	[Fact]
	public async Task GenerateAsync_RefusesAKeyTypeItDoesNotHave() =>
		await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
			new SshKeyGenerator().GenerateAsync(new SshKeyRequest { Type = (SshKeyType)42 }, Ct));

	private static Task<GeneratedSshKey> Generate(SshKeyType type, string? comment, string? passphrase) =>
		new SshKeyGenerator().GenerateAsync(new SshKeyRequest { Type = type, Comment = comment, Passphrase = passphrase }, Ct);

	private static BigInteger Number(byte[] mpint) => new(mpint, isUnsigned: true, isBigEndian: true);

	private static byte[] Blob(GeneratedSshKey key) => OpenSshKeyParser.Parse(key.PrivateKey).PublicBlob;

	/// <summary>The key bytes inside a public blob, past the algorithm name.</summary>
	private static byte[] PublicKeyBytes(byte[] publicBlob)
	{
		SshWireReader reader = new(publicBlob);
		Assert.True(reader.TryReadString(out ReadOnlySpan<byte> algorithm));
		Assert.Equal("ssh-ed25519", Encoding.ASCII.GetString(algorithm));
		Assert.True(reader.TryReadString(out ReadOnlySpan<byte> key));
		return key.ToArray();
	}
}
