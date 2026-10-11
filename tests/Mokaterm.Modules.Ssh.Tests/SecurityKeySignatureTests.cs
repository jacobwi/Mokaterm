using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Text;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Security;
using Mokaterm.Modules.Ssh.Agent;
using Mokaterm.Modules.Ssh.SecurityKeys;

namespace Mokaterm.Modules.Ssh.Tests;

/// <summary>
/// The signature blobs a security key login sends, laid out as PROTOCOL.u2f and ssh-sk.c write them, and the port of
/// sshd's checks that the signing tests rely on, proven first against OpenSSH's own test signatures.
/// </summary>
public sealed class SecurityKeySignatureTests
{
	[Theory]
	[InlineData(SshSignatures.Ecdsa)]
	[InlineData(SshSignatures.Ed25519)]
	[InlineData(SshSignatures.WebAuthnEcdsa)]
	public void Verifier_AcceptsOpenSshsOwnSecurityKeySignatures(string armored)
	{
		SshSignature signature = SshSignatures.Parse(armored);

		Assert.Equal(SshSignatures.TestNamespace, signature.Namespace);
		Assert.True(OpenSshSkVerifier.Verify(signature.PublicKey, signature.Signature, SshSignatures.Signed(signature.Namespace, SshSignatures.TestMessage)));
	}

	[Theory]
	[InlineData(SshSignatures.Ecdsa)]
	[InlineData(SshSignatures.Ed25519)]
	[InlineData(SshSignatures.WebAuthnEcdsa)]
	public void Verifier_RefusesThemForAnotherMessage(string armored)
	{
		SshSignature signature = SshSignatures.Parse(armored);
		byte[] other = Encoding.ASCII.GetBytes("This is a test, this is only a tesT");

		Assert.False(OpenSshSkVerifier.Verify(signature.PublicKey, signature.Signature, SshSignatures.Signed(signature.Namespace, other)));
	}

	[Fact]
	public void Encode_LaysAnEcdsaSignatureOutAsSshSkWritesIt()
	{
		SshSecurityKey key = Key(SecurityKeyAlgorithm.EcdsaP256);
		byte[] r = [0x80, .. Enumerable.Repeat((byte)1, 31)];
		byte[] s = [0x00, 0x00, 0x7F, .. Enumerable.Repeat((byte)2, 29)];

		byte[] blob = SecurityKeySignature.Encode(key, new SecurityKeyAssertion(AuthenticatorData("ssh:", 0x05, 0x01020304), Der(r, s)));

		SshWireReader reader = new(blob);
		Assert.True(reader.TryReadText(out string type));
		Assert.Equal("sk-ecdsa-sha2-nistp256@openssh.com", type);
		Assert.True(reader.TryReadString(out byte[] inner));
		Assert.True(reader.TryReadByte(out byte flags));
		Assert.Equal(0x05, flags);
		Assert.True(reader.TryReadUInt32(out uint counter));
		Assert.Equal(0x01020304u, counter);
		Assert.Equal(0, reader.Remaining);

		// Each half an mpint: a zero in front of a set top bit, and no zeros in front of a clear one.
		SshWireReader halves = new(inner);
		Assert.True(halves.TryReadString(out byte[] mpintR));
		Assert.Equal([0x00, .. r], mpintR);
		Assert.True(halves.TryReadString(out byte[] mpintS));
		Assert.Equal(s[2..], mpintS);
		Assert.Equal(0, halves.Remaining);
	}

	[Fact]
	public void Encode_LaysAnEd25519SignatureOutAsSshSkWritesIt()
	{
		SshSecurityKey key = Key(SecurityKeyAlgorithm.Ed25519);
		byte[] raw = [.. Enumerable.Range(0, 64).Select(i => (byte)i)];

		byte[] blob = SecurityKeySignature.Encode(key, new SecurityKeyAssertion(AuthenticatorData("ssh:", 0x01, 7), raw));

		byte[] expected = new SshWireWriter()
			.WriteString("sk-ssh-ed25519@openssh.com"u8)
			.WriteString(raw)
			.WriteByte(0x01)
			.WriteUInt32(7)
			.ToArray();
		Assert.Equal(expected, blob);
	}

	[Fact]
	public void EncodeWebAuthn_CarriesTheOriginTheClientDataAndTheExtensions()
	{
		SshSecurityKey key = Key(SecurityKeyAlgorithm.EcdsaP256);
		byte[] extensions = [0xA1, 0x61, (byte)'x', 0xF5];
		byte[] authenticatorData = [.. AuthenticatorData("ssh:", 0x81, 9), .. extensions];
		byte[] clientData = WebAuthnClientData.Build([1, 2, 3], "ssh:");

		byte[] blob = SecurityKeySignature.EncodeWebAuthn(key, new SecurityKeyAssertion(authenticatorData, Der([1], [2])), "ssh:", clientData);

		SshWireReader reader = new(blob);
		Assert.True(reader.TryReadText(out string type));
		Assert.Equal("webauthn-sk-ecdsa-sha2-nistp256@openssh.com", type);
		Assert.True(reader.TryReadString(out _));
		Assert.True(reader.TryReadByte(out byte flags));
		Assert.Equal(0x81, flags);
		Assert.True(reader.TryReadUInt32(out uint counter));
		Assert.Equal(9u, counter);
		Assert.True(reader.TryReadText(out string origin));
		Assert.Equal("ssh:", origin);
		Assert.True(reader.TryReadString(out byte[] carried));
		Assert.Equal(clientData, carried);
		Assert.True(reader.TryReadString(out byte[] carriedExtensions));
		Assert.Equal(extensions, carriedExtensions);
		Assert.Equal(0, reader.Remaining);
	}

	[Fact]
	public void ClientData_StartsWithThePreambleSshdRebuilds()
	{
		byte[] message = [0xFB, 0xFF, 0x00, 0x10];

		string json = Encoding.UTF8.GetString(WebAuthnClientData.Build(message, "ssh:"));

		// base64url without padding: + and / become - and _, and no = at the end.
		Assert.StartsWith("{\"type\":\"webauthn.get\",\"challenge\":\"-_8AEA\",\"origin\":\"ssh:\"", json, StringComparison.Ordinal);
	}

	[Fact]
	public void Encode_RefusesASignatureForAnotherApplication()
	{
		SecurityKeyException failure = Assert.Throws<SecurityKeyException>(() =>
			SecurityKeySignature.Encode(Key(SecurityKeyAlgorithm.Ed25519), new SecurityKeyAssertion(AuthenticatorData("ssh:other", 1, 1), new byte[64])));

		Assert.Contains("different application", failure.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Encode_RefusesExtensionsThePlainTypeCannotCarry() =>
		Assert.Throws<SecurityKeyException>(() =>
			SecurityKeySignature.Encode(Key(SecurityKeyAlgorithm.Ed25519), new SecurityKeyAssertion([.. AuthenticatorData("ssh:", 0x81, 1), 0xA0], new byte[64])));

	[Theory]
	[InlineData(new byte[] { 0x30, 0x03, 0x02, 0x01 })]
	[InlineData(new byte[] { 0x30, 0x06, 0x02, 0x01, 0x80, 0x02, 0x01, 0x01 })]
	[InlineData(new byte[] { 0x04, 0x00 })]
	public void Encode_RefusesAnEcdsaSignatureThatIsNotTwoPositiveIntegers(byte[] der) =>
		Assert.Throws<SecurityKeyException>(() =>
			SecurityKeySignature.Encode(Key(SecurityKeyAlgorithm.EcdsaP256), new SecurityKeyAssertion(AuthenticatorData("ssh:", 1, 1), der)));

	[Fact]
	public void Encode_RefusesAnEd25519SignatureOfTheWrongLength() =>
		Assert.Throws<SecurityKeyException>(() =>
			SecurityKeySignature.Encode(Key(SecurityKeyAlgorithm.Ed25519), new SecurityKeyAssertion(AuthenticatorData("ssh:", 1, 1), new byte[63])));

	[Fact]
	public void CanCarry_OnlyEcdsaKeysWithAnOriginJsonHoldsAsItIs()
	{
		Assert.True(WebAuthnClientData.CanCarry(Key(SecurityKeyAlgorithm.EcdsaP256)));
		Assert.False(WebAuthnClientData.CanCarry(Key(SecurityKeyAlgorithm.Ed25519)));
		Assert.False(WebAuthnClientData.CanCarry(Key(SecurityKeyAlgorithm.EcdsaP256) with { Application = "ssh:\"x" }));
	}

	internal static byte[] AuthenticatorData(string application, byte flags, uint counter) =>
	[
		.. SHA256.HashData(Encoding.UTF8.GetBytes(application)),
		flags,
		(byte)(counter >> 24),
		(byte)(counter >> 16),
		(byte)(counter >> 8),
		(byte)counter,
	];

	private static SshSecurityKey Key(SecurityKeyAlgorithm algorithm) => new()
	{
		Algorithm = algorithm,
		PublicKeyBlob = [1],
		Application = "ssh:",
		Flags = SecurityKeyFlags.UserPresenceRequired,
		KeyHandle = [1, 2, 3],
	};

	private static byte[] Der(byte[] r, byte[] s)
	{
		AsnWriter writer = new(AsnEncodingRules.DER);
		using (writer.PushSequence())
		{
			// A DER integer never carries redundant zeros, which is why a signature arrives without them.
			writer.WriteIntegerUnsigned(r.AsSpan().TrimStart((byte)0));
			writer.WriteIntegerUnsigned(s.AsSpan().TrimStart((byte)0));
		}

		return writer.Encode();
	}
}
