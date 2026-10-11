using System.Text;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Security;
using Mokaterm.Core.Keys;

namespace Mokaterm.Core.Tests.Keys;

/// <summary>
/// Making a security key the way <c>ssh-keygen -t ecdsa-sk</c> does, against a software authenticator that returns the
/// same authenticator data a FIDO2 key would: the request it gets, and the key file written from its answer.
/// </summary>
public sealed class SecurityKeyEnrollmentTests : IDisposable
{
	private readonly SoftwareSecurityKey _device = new();

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	public void Dispose() => _device.Dispose();

	[Theory]
	[InlineData(SshKeyType.EcdsaSecurityKey, SecurityKeyAlgorithm.EcdsaP256, "id_ecdsa_sk")]
	[InlineData(SshKeyType.Ed25519SecurityKey, SecurityKeyAlgorithm.Ed25519, "id_ed25519_sk")]
	public async Task GenerateAsync_WritesTheCredentialAsAnOpenSshSecurityKeyFile(SshKeyType type, SecurityKeyAlgorithm algorithm, string fileName)
	{
		GeneratedSshKey key = await new SshKeyGenerator(_device).GenerateAsync(new SshKeyRequest { Type = type, Comment = "me@box" }, Ct);

		SecurityKeyCredentialRequest request = Assert.Single(_device.Creations);
		Assert.Equal("ssh:", request.RelyingPartyId);
		Assert.Equal(algorithm, request.Algorithm);
		Assert.Equal(new byte[32], request.UserId);
		Assert.Equal("openssh", request.UserName);
		Assert.Equal(32, request.Challenge.Length);
		Assert.False(request.ResidentKey);
		Assert.False(request.RequireUserVerification);
		Assert.Equal(SecurityKeyProtection.None, request.Protection);

		Assert.Equal(fileName, key.SuggestedFileName);
		Assert.Equal(SecurityKeyTypes.KeyType(algorithm), key.PublicKey.Algorithm);
		Assert.EndsWith(" me@box", key.PublicKey.AuthorizedKeysLine, StringComparison.Ordinal);

		SshSecurityKey read = Read(key.PrivateKey, null);
		Assert.Equal(algorithm, read.Algorithm);
		Assert.Equal("ssh:", read.Application);
		Assert.Equal(SecurityKeyFlags.UserPresenceRequired, read.Flags);
		Assert.Equal("me@box", read.Comment);
		Assert.Equal(Convert.FromBase64String(key.PublicKey.AuthorizedKeysLine.Split(' ')[1]), read.PublicKeyBlob);
	}

	[Fact]
	public async Task GenerateAsync_KeepsTheCredentialIdAsTheKeyHandleAndThePublicKeyItCameWith()
	{
		GeneratedSshKey key = await new SshKeyGenerator(_device).GenerateAsync(new SshKeyRequest { Type = SshKeyType.EcdsaSecurityKey }, Ct);
		SshSecurityKey read = Read(key.PrivateKey, null);

		// The handle must be the credential the device made: an assertion for it has to be found and to verify.
		SecurityKeyAssertion assertion = await _device.GetAssertionAsync(
			new SecurityKeyAssertionRequest { RelyingPartyId = "ssh:", ClientData = [1, 2, 3], CredentialId = read.KeyHandle },
			Ct);

		Assert.True(AuthenticatorDataHeader.TryRead(assertion.AuthenticatorData, "ssh:", out _));
		Assert.Equal(SecurityKeyFile.PublicBlob(SecurityKeyAlgorithm.EcdsaP256, PointOf(read), "ssh:"), read.PublicKeyBlob);
	}

	[Fact]
	public async Task GenerateAsync_AsksForProtectionTheWayOpenSshDoes()
	{
		SshKeyGenerator generator = new(_device);

		await generator.GenerateAsync(new SshKeyRequest { Type = SshKeyType.EcdsaSecurityKey, ResidentKey = true }, Ct);
		await generator.GenerateAsync(new SshKeyRequest { Type = SshKeyType.EcdsaSecurityKey, RequireUserVerification = true, Application = "ssh:work" }, Ct);

		Assert.Equal(SecurityKeyProtection.UserVerificationOptionalWithCredentialIdList, _device.Creations[0].Protection);
		Assert.True(_device.Creations[0].ResidentKey);
		Assert.Equal(SecurityKeyProtection.UserVerificationRequired, _device.Creations[1].Protection);
		Assert.True(_device.Creations[1].RequireUserVerification);
		Assert.Equal("ssh:work", _device.Creations[1].RelyingPartyId);
	}

	[Fact]
	public async Task GenerateAsync_RecordsTheChoicesInTheFlagsByte()
	{
		GeneratedSshKey key = await new SshKeyGenerator(_device).GenerateAsync(
			new SshKeyRequest { Type = SshKeyType.Ed25519SecurityKey, ResidentKey = true, RequireUserVerification = true, Application = "ssh:git" },
			Ct);

		SshSecurityKey read = Read(key.PrivateKey, null);

		Assert.Equal(SecurityKeyFlags.UserPresenceRequired | SecurityKeyFlags.UserVerificationRequired | SecurityKeyFlags.ResidentKey, read.Flags);
		Assert.Equal("ssh:git", read.Application);
	}

	[Fact]
	public async Task GenerateAsync_EncryptsTheFileWithThePassphrase()
	{
		GeneratedSshKey key = await new SshKeyGenerator(_device).GenerateAsync(
			new SshKeyRequest { Type = SshKeyType.EcdsaSecurityKey, Passphrase = "pass phrase" },
			Ct);

		Assert.True(key.HasPassphrase);
		Assert.Equal("aes256-ctr", OpenSshKeyParser.Parse(key.PrivateKey).Cipher);
		Assert.Equal(PrivateKeyStatus.PassphraseRequired, new SshSecurityKeyReader().Read(Encoding.UTF8.GetBytes(key.PrivateKey), null).Status);
		Assert.Equal(SecurityKeyAlgorithm.EcdsaP256, Read(key.PrivateKey, "pass phrase").Algorithm);
	}

	[Fact]
	public async Task GenerateAsync_RefusesWithoutAnAuthenticator()
	{
		SecurityKeyException failure = await Assert.ThrowsAsync<SecurityKeyException>(() =>
			new SshKeyGenerator().GenerateAsync(new SshKeyRequest { Type = SshKeyType.EcdsaSecurityKey }, Ct));

		Assert.Equal(SecurityKeyFailure.Unavailable, failure.Failure);
		Assert.Contains("desktop app", failure.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task GenerateAsync_RefusesAnApplicationSshKeygenWouldRefuse()
	{
		await Assert.ThrowsAsync<ArgumentException>(() =>
			new SshKeyGenerator(_device).GenerateAsync(new SshKeyRequest { Type = SshKeyType.EcdsaSecurityKey, Application = "https://example.com" }, Ct));

		Assert.Empty(_device.Creations);
	}

	[Fact]
	public async Task GenerateAsync_RefusesACredentialMadeForAnotherApplication()
	{
		_device.WrongRelyingParty = "ssh:other";

		await Assert.ThrowsAsync<SecurityKeyException>(() =>
			new SshKeyGenerator(_device).GenerateAsync(new SshKeyRequest { Type = SshKeyType.EcdsaSecurityKey }, Ct));
	}

	[Fact]
	public async Task GenerateAsync_SaysSoWhenTheDeviceMadeAnotherKindOfKey()
	{
		_device.MakeInstead = SecurityKeyAlgorithm.EcdsaP256;

		SecurityKeyException failure = await Assert.ThrowsAsync<SecurityKeyException>(() =>
			new SshKeyGenerator(_device).GenerateAsync(new SshKeyRequest { Type = SshKeyType.Ed25519SecurityKey }, Ct));

		Assert.Equal(SecurityKeyFailure.NotSupported, failure.Failure);
	}

	[Fact]
	public async Task GenerateAsync_PassesTheAuthenticatorsOwnFailureOn()
	{
		_device.Failure = new SecurityKeyException(SecurityKeyFailure.Cancelled, "closed");

		SecurityKeyException failure = await Assert.ThrowsAsync<SecurityKeyException>(() =>
			new SshKeyGenerator(_device).GenerateAsync(new SshKeyRequest { Type = SshKeyType.EcdsaSecurityKey }, Ct));

		Assert.Equal(SecurityKeyFailure.Cancelled, failure.Failure);
	}

	[Fact]
	public async Task ReadCredential_RefusesACredentialIdTheDataDoesNotCarry()
	{
		(byte[] id, _) = _device.Enrol(SecurityKeyAlgorithm.EcdsaP256, "ssh:");
		SecurityKeyCredential made = await _device.MakeCredentialAsync(
			new SecurityKeyCredentialRequest
			{
				RelyingPartyId = "ssh:",
				Algorithm = SecurityKeyAlgorithm.EcdsaP256,
				Challenge = [1],
				UserId = new byte[32],
				UserName = "openssh",
			},
			Ct);

		SecurityKeyCredential mismatched = made with { CredentialId = id };

		Assert.Throws<SecurityKeyException>(() => SecurityKeyEnrollment.ReadCredential(mismatched, "ssh:", SecurityKeyAlgorithm.EcdsaP256));
	}

	[Fact]
	public void CoseKey_SkipsLabelsItDoesNotUse()
	{
		byte[] x = [.. Enumerable.Repeat((byte)7, 32)];

		// An extra text label with an array value before the key, as an authenticator may add.
		byte[] cose = [0xA5, 0x63, (byte)'f', (byte)'o', (byte)'o', 0x82, 0x01, 0x40, 0x01, 0x01, 0x03, 0x27, 0x20, 0x06, 0x21, 0x58, 0x20, .. x];

		Assert.Equal(x, CoseKey.ReadPublicKey(cose, SecurityKeyAlgorithm.Ed25519));
	}

	[Theory]
	[InlineData(new byte[] { 0xBF, 0x01, 0x01, 0xFF })]
	[InlineData(new byte[] { 0xA1, 0x21, 0x5A, 0xFF, 0xFF, 0xFF, 0xFF })]
	[InlineData(new byte[] { 0xA1 })]
	public void CoseKey_RefusesWhatIsNotADefiniteCborMap(byte[] cose) =>
		Assert.Throws<SecurityKeyException>(() => CoseKey.ReadPublicKey(cose, SecurityKeyAlgorithm.Ed25519));

	private static byte[] PointOf(SshSecurityKey key)
	{
		// string type, string curve, string point, string application
		int offset = 4 + SecurityKeyTypes.EcdsaKeyType.Length + 4 + "nistp256".Length;
		return key.PublicKeyBlob.AsSpan(offset + 4, 65).ToArray();
	}

	private static SshSecurityKey Read(string text, string? passphrase)
	{
		SecurityKeyFileResult result = new SshSecurityKeyReader().Read(Encoding.UTF8.GetBytes(text), passphrase);
		Assert.True(result.IsValid, result.Error);
		return result.Key;
	}
}
