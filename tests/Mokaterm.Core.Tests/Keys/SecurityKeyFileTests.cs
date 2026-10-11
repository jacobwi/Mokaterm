using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Core.Keys;
using Mokaterm.Core.Security;

namespace Mokaterm.Core.Tests.Keys;

/// <summary>
/// Security key files: OpenSSH's own test keys read back exactly as <c>ssh-keygen</c> prints them, and the writer's
/// field order held against PROTOCOL.u2f byte by byte.
/// </summary>
public sealed class SecurityKeyFileTests
{
	private static readonly SshSecurityKeyReader Reader = new();

	[Fact]
	public void Read_OpensOpenSshsEcdsaSecurityKeyFile()
	{
		SshSecurityKey key = ReadValid(SecurityKeyVectors.EcdsaSk, passphrase: null);

		Assert.Equal(SecurityKeyAlgorithm.EcdsaP256, key.Algorithm);
		Assert.Equal(SecurityKeyTypes.EcdsaKeyType, key.KeyType);
		Assert.Equal("ssh:", key.Application);
		Assert.Equal(SecurityKeyFlags.UserPresenceRequired, key.Flags);
		Assert.Equal("ECDSA-SK test key #1", key.Comment);
		Assert.Equal(PublicBlob(SecurityKeyVectors.EcdsaSkPublicKey), key.PublicKeyBlob);
		Assert.Equal(SecurityKeyVectors.EcdsaSkFingerprint, Fingerprint(key.PublicKeyBlob));

		// OpenSSH's software middleware keeps the private key in the handle, which shows the handle came out whole.
		Assert.StartsWith("-----BEGIN EC PRIVATE KEY-----", Encoding.ASCII.GetString(key.KeyHandle), StringComparison.Ordinal);
	}

	[Fact]
	public void Read_OpensOpenSshsEd25519SecurityKeyFile()
	{
		SshSecurityKey key = ReadValid(SecurityKeyVectors.Ed25519Sk, passphrase: null);

		Assert.Equal(SecurityKeyAlgorithm.Ed25519, key.Algorithm);
		Assert.Equal(SecurityKeyTypes.Ed25519KeyType, key.KeyType);
		Assert.Equal("ssh:", key.Application);
		Assert.Equal(SecurityKeyFlags.UserPresenceRequired, key.Flags);
		Assert.Equal("ED25519-SK test key #1", key.Comment);
		Assert.Equal(PublicBlob(SecurityKeyVectors.Ed25519SkPublicKey), key.PublicKeyBlob);
		Assert.Equal(SecurityKeyVectors.Ed25519SkFingerprint, Fingerprint(key.PublicKeyBlob));
		Assert.Equal(64, key.KeyHandle.Length);
	}

	[Theory]
	[InlineData(SecurityKeyVectors.EcdsaSkEncrypted, SecurityKeyVectors.EcdsaSk)]
	[InlineData(SecurityKeyVectors.Ed25519SkEncrypted, SecurityKeyVectors.Ed25519Sk)]
	public void Read_DecryptsAPassphraseProtectedFileToTheSameKey(string encrypted, string plain)
	{
		SshSecurityKey expected = ReadValid(plain, passphrase: null);

		SshSecurityKey key = ReadValid(encrypted, SecurityKeyVectors.Passphrase);

		Assert.Equal(expected.PublicKeyBlob, key.PublicKeyBlob);
		Assert.Equal(expected.KeyHandle, key.KeyHandle);
		Assert.Equal(expected.Flags, key.Flags);
		Assert.Equal(expected.Application, key.Application);
		Assert.Equal(expected.Comment, key.Comment);
	}

	[Fact]
	public void Read_TellsAMissingPassphraseFromAWrongOne()
	{
		Assert.Equal(PrivateKeyStatus.PassphraseRequired, Read(SecurityKeyVectors.EcdsaSkEncrypted, null).Status);
		Assert.Equal(PrivateKeyStatus.WrongPassphrase, Read(SecurityKeyVectors.EcdsaSkEncrypted, "not it").Status);
	}

	[Fact]
	public void ReadPublicKey_PrintsTheLineSshKeygenPrints()
	{
		SshKeyGenerator generator = new();

		Assert.Equal(SecurityKeyVectors.EcdsaSkPublicKey, generator.ReadPublicKey(SecurityKeyVectors.EcdsaSk)?.AuthorizedKeysLine);
		Assert.Equal(SecurityKeyVectors.Ed25519SkPublicKey, generator.ReadPublicKey(SecurityKeyVectors.Ed25519Sk)?.AuthorizedKeysLine);
		Assert.Equal(SecurityKeyVectors.EcdsaSkFingerprint, generator.ReadPublicKey(SecurityKeyVectors.EcdsaSkEncrypted)?.Fingerprint);
	}

	[Fact]
	public void PublicBlob_WritesTheEcdsaFieldsInProtocolOrder()
	{
		byte[] point = [0x04, .. Enumerable.Repeat((byte)0xAB, 64)];

		byte[] blob = SecurityKeyFile.PublicBlob(SecurityKeyAlgorithm.EcdsaP256, point, "ssh:");

		byte[] expected =
		[
			.. Str("sk-ecdsa-sha2-nistp256@openssh.com"u8),
			.. Str("nistp256"u8),
			.. Str(point),
			.. Str("ssh:"u8),
		];
		Assert.Equal(expected, blob);
	}

	[Fact]
	public void PrivateBlob_AddsFlagsHandleAndReservedAfterThePublicFields()
	{
		byte[] publicKey = [.. Enumerable.Range(0, 32).Select(i => (byte)i)];
		byte[] handle = [9, 8, 7];

		using PinnedBytes blob = SecurityKeyFile.PrivateBlob(
			SecurityKeyAlgorithm.Ed25519,
			publicKey,
			"ssh:work",
			SecurityKeyFlags.UserPresenceRequired | SecurityKeyFlags.ResidentKey,
			handle);

		byte[] expected =
		[
			.. Str("sk-ssh-ed25519@openssh.com"u8),
			.. Str(publicKey),
			.. Str("ssh:work"u8),
			0x21,
			.. Str(handle),
			.. Str([]),
		];
		Assert.Equal(expected, blob.Span.ToArray());
	}

	[Theory]
	[InlineData(SecurityKeyAlgorithm.EcdsaP256, "")]
	[InlineData(SecurityKeyAlgorithm.Ed25519, "pass phrase")]
	public void Written_ReadsBackThroughTheReader(SecurityKeyAlgorithm algorithm, string passphrase)
	{
		byte[] publicKey = PublicKeyFor(algorithm);
		byte[] handle = RandomNumberGenerator.GetBytes(64);
		SecurityKeyFlags flags = SecurityKeyFlags.UserPresenceRequired | SecurityKeyFlags.UserVerificationRequired;
		string text = Write(algorithm, publicKey, "ssh:", flags, handle, "me@box", passphrase);

		SshSecurityKey key = ReadValid(text, passphrase.Length == 0 ? null : passphrase);

		Assert.Equal(algorithm, key.Algorithm);
		Assert.Equal(SecurityKeyFile.PublicBlob(algorithm, publicKey, "ssh:"), key.PublicKeyBlob);
		Assert.Equal(handle, key.KeyHandle);
		Assert.Equal(flags, key.Flags);
		Assert.True(key.RequiresUserVerification);
		Assert.Equal("me@box", key.Comment);
	}

	[Fact]
	public void Read_RefusesAnOrdinaryKey()
	{
		SecurityKeyFileResult result = Read(ReferenceKeys.Ed25519, null);

		Assert.Equal(PrivateKeyStatus.Invalid, result.Status);
		Assert.Contains("not a security key", result.Error, StringComparison.Ordinal);
	}

	[Fact]
	public void Read_RefusesAFileWhoseHalvesDisagree()
	{
		byte[] publicKey = PublicKeyFor(SecurityKeyAlgorithm.Ed25519);
		byte[] otherKey = PublicKeyFor(SecurityKeyAlgorithm.Ed25519);
		using PinnedBytes privateBlob = SecurityKeyFile.PrivateBlob(SecurityKeyAlgorithm.Ed25519, otherKey, "ssh:", SecurityKeyFlags.UserPresenceRequired, [1, 2, 3]);
		string text = OpenSshPrivateKeyFile.Write(SecurityKeyFile.PublicBlob(SecurityKeyAlgorithm.Ed25519, publicKey, "ssh:"), privateBlob.Span, "", []);

		SecurityKeyFileResult result = Read(text, null);

		Assert.Equal(PrivateKeyStatus.Invalid, result.Status);
		Assert.Contains("does not belong", result.Error, StringComparison.Ordinal);
	}

	[Fact]
	public void Read_RefusesAPointThatIsNotOnTheCurve()
	{
		byte[] point = [0x04, .. Enumerable.Repeat((byte)1, 64)];
		using PinnedBytes privateBlob = SecurityKeyFile.PrivateBlob(SecurityKeyAlgorithm.EcdsaP256, point, "ssh:", SecurityKeyFlags.UserPresenceRequired, [1]);
		string text = OpenSshPrivateKeyFile.Write(SecurityKeyFile.PublicBlob(SecurityKeyAlgorithm.EcdsaP256, point, "ssh:"), privateBlob.Span, "", []);

		Assert.Equal(PrivateKeyStatus.Invalid, Read(text, null).Status);
	}

	[Fact]
	public void Read_SaysCertificatesAreNotSupported()
	{
		byte[] publicKey = PublicKeyFor(SecurityKeyAlgorithm.Ed25519);
		byte[] blob = [.. Str("sk-ssh-ed25519-cert-v01@openssh.com"u8), .. Str(publicKey)];
		string text = OpenSshPrivateKeyFile.Write(blob, blob, "", []);

		Assert.Equal(PrivateKeyStatus.Unsupported, Read(text, null).Status);
	}

	[Fact]
	public void Read_NamesACipherItCannotRead()
	{
		string text = Container("chacha20-poly1305@openssh.com", "bcrypt", KdfOptions(16), new byte[32]);

		SecurityKeyFileResult result = Read(text, "anything");

		Assert.Equal(PrivateKeyStatus.Unsupported, result.Status);
		Assert.Contains("chacha20-poly1305@openssh.com", result.Error, StringComparison.Ordinal);
	}

	[Fact]
	public void Read_RefusesKeyDerivationRoundsThatWouldTakeMinutes()
	{
		string text = Container("aes256-ctr", "bcrypt", KdfOptions(OpenSshPrivateKeyFile.MaxKdfRounds + 1), new byte[32]);

		SecurityKeyFileResult result = Read(text, "anything");

		Assert.Equal(PrivateKeyStatus.Unsupported, result.Status);
		Assert.Contains("rounds", result.Error, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("aes128-ctr")]
	[InlineData("aes192-ctr")]
	[InlineData("aes256-cbc")]
	public void Read_DecryptsTheOtherAesCiphersSshKeygenOffers(string cipher)
	{
		byte[] publicKey = PublicKeyFor(SecurityKeyAlgorithm.EcdsaP256);
		byte[] publicBlob = SecurityKeyFile.PublicBlob(SecurityKeyAlgorithm.EcdsaP256, publicKey, "ssh:");
		using PinnedBytes privateBlob = SecurityKeyFile.PrivateBlob(SecurityKeyAlgorithm.EcdsaP256, publicKey, "ssh:", SecurityKeyFlags.UserPresenceRequired, [5, 6]);
		byte[] section = Section(privateBlob.Span, "cbc test", 16);
		byte[] salt = RandomNumberGenerator.GetBytes(16);
		int keyLength = cipher.StartsWith("aes128", StringComparison.Ordinal) ? 16 : cipher.StartsWith("aes192", StringComparison.Ordinal) ? 24 : 32;
		byte[] keyAndIv = new byte[keyLength + 16];
		BcryptPbkdf.DeriveKey("secret"u8, salt, 4, keyAndIv);
		if (cipher.EndsWith("-cbc", StringComparison.Ordinal))
		{
			using Aes aes = Aes.Create();
			aes.Key = keyAndIv[..keyLength];
			section = aes.EncryptCbc(section, keyAndIv.AsSpan(keyLength), PaddingMode.None);
		}
		else
		{
			AesCtr.Transform(keyAndIv.AsSpan(0, keyLength), keyAndIv.AsSpan(keyLength), section);
		}

		string text = Container(cipher, "bcrypt", [.. Str(salt), 0, 0, 0, 4], section, publicBlob);

		SshSecurityKey key = ReadValid(text, "secret");

		Assert.Equal(publicBlob, key.PublicKeyBlob);
		Assert.Equal("cbc test", key.Comment);
	}

	[Fact]
	public void Read_RefusesPaddingThatIsNotOpenSshs()
	{
		byte[] publicKey = PublicKeyFor(SecurityKeyAlgorithm.Ed25519);
		using PinnedBytes privateBlob = SecurityKeyFile.PrivateBlob(SecurityKeyAlgorithm.Ed25519, publicKey, "ssh:", SecurityKeyFlags.UserPresenceRequired, [7]);
		byte[] section = Section(privateBlob.Span, "c", 8);
		section[^1] = 0xEE;
		string text = Container("none", "none", [], section, SecurityKeyFile.PublicBlob(SecurityKeyAlgorithm.Ed25519, publicKey, "ssh:"));

		Assert.Equal(PrivateKeyStatus.Invalid, Read(text, null).Status);
	}

	[Theory]
	[InlineData("ssh:", true)]
	[InlineData("ssh:work.example", true)]
	[InlineData("", false)]
	[InlineData("https://example.com", false)]
	[InlineData("ssh:two words", false)]
	[InlineData("ssh:\"quoted\"", false)]
	public void ValidateApplication_FollowsSshKeygensPrefixRule(string application, bool valid) =>
		Assert.Equal(valid, SecurityKeyTypes.ValidateApplication(application) is null);

	internal static byte[] PublicKeyFor(SecurityKeyAlgorithm algorithm)
	{
		if (algorithm == SecurityKeyAlgorithm.Ed25519)
		{
			return RandomNumberGenerator.GetBytes(32);
		}

		using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
		ECParameters parameters = key.ExportParameters(false);
		return [0x04, .. parameters.Q.X!, .. parameters.Q.Y!];
	}

	private static SecurityKeyFileResult Read(string text, string? passphrase) => Reader.Read(Encoding.UTF8.GetBytes(text), passphrase);

	private static SshSecurityKey ReadValid(string text, string? passphrase)
	{
		SecurityKeyFileResult result = Read(text, passphrase);
		Assert.True(result.IsValid, result.Error);
		return result.Key;
	}

	private static string Write(SecurityKeyAlgorithm algorithm, byte[] publicKey, string application, SecurityKeyFlags flags, byte[] handle, string comment, string passphrase)
	{
		using PinnedBytes privateBlob = SecurityKeyFile.PrivateBlob(algorithm, publicKey, application, flags, handle);
		return OpenSshPrivateKeyFile.Write(
			SecurityKeyFile.PublicBlob(algorithm, publicKey, application),
			privateBlob.Span,
			comment,
			Encoding.UTF8.GetBytes(passphrase),
			rounds: 2);
	}

	private static byte[] PublicBlob(string line) => Convert.FromBase64String(line.Split(' ')[1]);

	private static string Fingerprint(byte[] blob) => "SHA256:" + Convert.ToBase64String(SHA256.HashData(blob)).TrimEnd('=');

	private static byte[] Str(ReadOnlySpan<byte> value)
	{
		byte[] result = new byte[4 + value.Length];
		BinaryPrimitives.WriteUInt32BigEndian(result, (uint)value.Length);
		value.CopyTo(result.AsSpan(4));
		return result;
	}

	private static byte[] KdfOptions(uint rounds)
	{
		byte[] options = [.. Str(new byte[16]), 0, 0, 0, 0];
		BinaryPrimitives.WriteUInt32BigEndian(options.AsSpan(options.Length - 4), rounds);
		return options;
	}

	/// <summary>A private section as OpenSSH lays it out: two equal check words, the key, the comment, then 1, 2, 3 padding.</summary>
	private static byte[] Section(ReadOnlySpan<byte> privateBlob, string comment, int blockSize)
	{
		List<byte> section = [0x11, 0x22, 0x33, 0x44, 0x11, 0x22, 0x33, 0x44, .. privateBlob, .. Str(Encoding.UTF8.GetBytes(comment))];
		for (int pad = 1; section.Count % blockSize != 0; pad++)
		{
			section.Add((byte)pad);
		}

		return [.. section];
	}

	private static string Container(string cipher, string kdf, byte[] kdfOptions, byte[] section, byte[]? publicBlob = null)
	{
		publicBlob ??= SecurityKeyFile.PublicBlob(SecurityKeyAlgorithm.Ed25519, PublicKeyFor(SecurityKeyAlgorithm.Ed25519), "ssh:");
		ArrayBufferWriter<byte> body = new();
		body.Write("openssh-key-v1\0"u8);
		body.Write(Str(Encoding.ASCII.GetBytes(cipher)));
		body.Write(Str(Encoding.ASCII.GetBytes(kdf)));
		body.Write(Str(kdfOptions));
		body.Write<byte>([0, 0, 0, 1]);
		body.Write(Str(publicBlob));
		body.Write(Str(section));
		return $"{OpenSshPrivateKeyFile.BeginMarker}\n{Convert.ToBase64String(body.WrittenSpan)}\n{OpenSshPrivateKeyFile.EndMarker}\n";
	}
}
