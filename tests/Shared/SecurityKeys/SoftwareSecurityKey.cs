using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Security;
using BouncyEd25519 = Org.BouncyCastle.Math.EC.Rfc8032.Ed25519;

namespace Mokaterm.Tests.Shared;

/// <summary>
/// A FIDO2 authenticator in software, which signs exactly what a real one signs: the authenticator data followed by
/// the SHA-256 of the client data, with ECDSA in DER or with Ed25519. It stands in for webauthn.dll so the key file
/// writer and the SSH signer can be proven end to end without a device. Its private keys are made per test.
/// </summary>
internal sealed class SoftwareSecurityKey : ISecurityKeyAuthenticator, IDisposable
{
	private const byte UserPresent = 0x01;
	private const byte UserVerified = 0x04;
	private const byte AttestedCredentialData = 0x40;
	private const byte ExtensionData = 0x80;

	private readonly Dictionary<string, Credential> _credentials = [];
	private readonly List<SecurityKeyAssertionRequest> _assertions = [];
	private readonly List<SecurityKeyCredentialRequest> _creations = [];

	public string Description => "Software security key for tests";

	public bool IsAvailable { get; init; } = true;

	/// <summary>Refuses client data that is not JSON, the way a platform that insisted on WebAuthn's own form would.</summary>
	public bool RefuseRawClientData { get; set; }

	/// <summary>Signs extension data into every assertion, which only the WebAuthn signature type can carry.</summary>
	public byte[] AssertionExtensions { get; set; } = [];

	/// <summary>Answers every call with this instead of signing.</summary>
	public SecurityKeyException? Failure { get; set; }

	/// <summary>Makes the next credential answer with this COSE algorithm instead of the one asked for.</summary>
	public SecurityKeyAlgorithm? MakeInstead { get; set; }

	/// <summary>Reports a relying party other than the one asked for in the next credential's data.</summary>
	public string? WrongRelyingParty { get; set; }

	public uint Counter { get; private set; } = 41;

	public IReadOnlyList<SecurityKeyAssertionRequest> Assertions => _assertions;

	public IReadOnlyList<SecurityKeyCredentialRequest> Creations => _creations;

	/// <summary>Adds a credential made outside <see cref="MakeCredentialAsync"/>, returning its id and SSH form public key.</summary>
	public (byte[] CredentialId, byte[] PublicKey) Enrol(SecurityKeyAlgorithm algorithm, string relyingPartyId)
	{
		Credential credential = Credential.Create(algorithm, relyingPartyId);
		_credentials[Convert.ToHexString(credential.Id)] = credential;
		return (credential.Id, credential.PublicKey);
	}

	public Task<SecurityKeyCredential> MakeCredentialAsync(SecurityKeyCredentialRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		cancellationToken.ThrowIfCancellationRequested();
		_creations.Add(request);
		if (Failure is not null)
		{
			throw Failure;
		}

		Credential credential = Credential.Create(MakeInstead ?? request.Algorithm, request.RelyingPartyId);
		_credentials[Convert.ToHexString(credential.Id)] = credential;
		MakeInstead = null;

		byte flags = (byte)(UserPresent | AttestedCredentialData | (request.RequireUserVerification ? UserVerified : 0));
		List<byte> data = [.. SHA256.HashData(Encoding.UTF8.GetBytes(WrongRelyingParty ?? request.RelyingPartyId)), flags, 0, 0, 0, 0];
		WrongRelyingParty = null;
		data.AddRange(new byte[16]);
		data.Add((byte)(credential.Id.Length >> 8));
		data.Add((byte)credential.Id.Length);
		data.AddRange(credential.Id);
		data.AddRange(credential.CoseKey());
		return Task.FromResult(new SecurityKeyCredential([.. data], credential.Id));
	}

	public Task<SecurityKeyAssertion> GetAssertionAsync(SecurityKeyAssertionRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		cancellationToken.ThrowIfCancellationRequested();
		_assertions.Add(request);
		if (Failure is not null)
		{
			throw Failure;
		}

		if (RefuseRawClientData && (request.ClientData.Length == 0 || request.ClientData[0] != (byte)'{'))
		{
			throw new SecurityKeyException(SecurityKeyFailure.InvalidRequest, "The client data is not JSON.");
		}

		if (!_credentials.TryGetValue(Convert.ToHexString(request.CredentialId), out Credential? credential)
			|| credential.RelyingPartyId != request.RelyingPartyId)
		{
			throw new SecurityKeyException(SecurityKeyFailure.NoCredential, "No such credential.");
		}

		Counter++;
		byte flags = (byte)(UserPresent | (request.RequireUserVerification ? UserVerified : 0) | (AssertionExtensions.Length > 0 ? ExtensionData : 0));
		byte[] counter = new byte[4];
		BinaryPrimitives.WriteUInt32BigEndian(counter, Counter);
		byte[] authenticatorData = [.. SHA256.HashData(Encoding.UTF8.GetBytes(request.RelyingPartyId)), flags, .. counter, .. AssertionExtensions];
		byte[] signed = [.. authenticatorData, .. SHA256.HashData(request.ClientData)];
		return Task.FromResult(new SecurityKeyAssertion(authenticatorData, credential.Sign(signed)));
	}

	public void Dispose()
	{
		foreach (Credential credential in _credentials.Values)
		{
			credential.Dispose();
		}
	}

	private sealed class Credential : IDisposable
	{
		private readonly ECDsa? _ecdsa;
		private readonly byte[]? _ed25519Secret;

		private Credential(SecurityKeyAlgorithm algorithm, string relyingPartyId, ECDsa? ecdsa, byte[]? ed25519Secret, byte[] publicKey)
		{
			Algorithm = algorithm;
			RelyingPartyId = relyingPartyId;
			_ecdsa = ecdsa;
			_ed25519Secret = ed25519Secret;
			PublicKey = publicKey;
			Id = RandomNumberGenerator.GetBytes(48);
		}

		public SecurityKeyAlgorithm Algorithm { get; }

		public string RelyingPartyId { get; }

		public byte[] Id { get; }

		/// <summary>An uncompressed point for ECDSA, 32 bytes for Ed25519: the form the SSH key blob carries.</summary>
		public byte[] PublicKey { get; }

		public static Credential Create(SecurityKeyAlgorithm algorithm, string relyingPartyId)
		{
			if (algorithm == SecurityKeyAlgorithm.EcdsaP256)
			{
				ECDsa ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
				ECParameters parameters = ecdsa.ExportParameters(includePrivateParameters: false);
				return new Credential(algorithm, relyingPartyId, ecdsa, null, [0x04, .. parameters.Q.X!, .. parameters.Q.Y!]);
			}

			byte[] secret = RandomNumberGenerator.GetBytes(BouncyEd25519.SecretKeySize);
			byte[] publicKey = new byte[BouncyEd25519.PublicKeySize];
			BouncyEd25519.GeneratePublicKey(secret, 0, publicKey, 0);
			return new Credential(algorithm, relyingPartyId, null, secret, publicKey);
		}

		/// <summary>The COSE_Key a CTAP2 authenticator reports, in its canonical CBOR.</summary>
		public byte[] CoseKey() => Algorithm == SecurityKeyAlgorithm.EcdsaP256
			? [0xA5, 0x01, 0x02, 0x03, 0x26, 0x20, 0x01, 0x21, 0x58, 0x20, .. PublicKey.AsSpan(1, 32), 0x22, 0x58, 0x20, .. PublicKey.AsSpan(33, 32)]
			: [0xA4, 0x01, 0x01, 0x03, 0x27, 0x20, 0x06, 0x21, 0x58, 0x20, .. PublicKey];

		public byte[] Sign(byte[] signed)
		{
			if (_ecdsa is not null)
			{
				return _ecdsa.SignData(signed, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
			}

			byte[] signature = new byte[BouncyEd25519.SignatureSize];
			BouncyEd25519.Sign(_ed25519Secret!, 0, signed, 0, signed.Length, signature, 0);
			return signature;
		}

		public void Dispose()
		{
			_ecdsa?.Dispose();
			if (_ed25519Secret is not null)
			{
				CryptographicOperations.ZeroMemory(_ed25519Secret);
			}
		}
	}
}
