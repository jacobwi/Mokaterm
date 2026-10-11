using System.Buffers.Binary;
using System.Security.Cryptography;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Security;
using Mokaterm.Core.Security;

namespace Mokaterm.Core.Keys;

/// <summary>
/// Makes a security key credential the way <c>ssh-keygen -t ecdsa-sk</c> does through its FIDO middleware, then writes
/// the result as the same key file: the device keeps the private key, the file keeps the public key, the application,
/// the flags and the credential id.
/// </summary>
internal static class SecurityKeyEnrollment
{
	/// <summary>The user name OpenSSH enrols with, which a resident key shows on the device.</summary>
	public const string UserName = "openssh";

	/// <summary>OpenSSH's default user id is 32 zero bytes, and <c>ssh-keygen -K</c> loads resident keys by it.</summary>
	public const int UserIdLength = 32;

	private const int ChallengeLength = 32;
	private const int AaguidLength = 16;
	private const int CredentialIdLengthSize = 2;

	/// <summary>A touch can take a while: the user may have to find the key or plug it in.</summary>
	private static readonly TimeSpan PromptTimeout = TimeSpan.FromMinutes(2);

	public static async Task<GeneratedSshKey> GenerateAsync(
		ISecurityKeyAuthenticator? authenticator,
		SshKeyRequest request,
		string comment,
		CancellationToken cancellationToken)
	{
		if (authenticator is null)
		{
			throw new SecurityKeyException(SecurityKeyFailure.Unavailable, "Security keys can only be made in the desktop app.");
		}

		if (!authenticator.IsAvailable)
		{
			throw new SecurityKeyException(SecurityKeyFailure.Unavailable, "This computer offers no way to reach a security key.");
		}

		SecurityKeyAlgorithm algorithm = request.Type == SshKeyType.Ed25519SecurityKey ? SecurityKeyAlgorithm.Ed25519 : SecurityKeyAlgorithm.EcdsaP256;
		string application = string.IsNullOrEmpty(request.Application) ? SecurityKeyTypes.DefaultApplication : request.Application;
		if (SecurityKeyTypes.ValidateApplication(application) is { } problem)
		{
			throw new ArgumentException(problem, nameof(request));
		}

		SecurityKeyFlags flags = SecurityKeyFlags.UserPresenceRequired;
		if (request.RequireUserVerification)
		{
			flags |= SecurityKeyFlags.UserVerificationRequired;
		}

		if (request.ResidentKey)
		{
			flags |= SecurityKeyFlags.ResidentKey;
		}

		SecurityKeyCredential credential = await authenticator.MakeCredentialAsync(
			new SecurityKeyCredentialRequest
			{
				RelyingPartyId = application,
				Algorithm = algorithm,
				Challenge = RandomNumberGenerator.GetBytes(ChallengeLength),
				UserId = new byte[UserIdLength],
				UserName = UserName,
				ResidentKey = request.ResidentKey,
				RequireUserVerification = request.RequireUserVerification,
				Protection = ProtectionFor(request.ResidentKey, request.RequireUserVerification),
				Timeout = PromptTimeout,
			},
			cancellationToken);

		(byte[] credentialId, byte[] publicKey) = ReadCredential(credential, application, algorithm);
		byte[] publicBlob = SecurityKeyFile.PublicBlob(algorithm, publicKey, application);
		string fileName = algorithm == SecurityKeyAlgorithm.Ed25519 ? "id_ed25519_sk" : "id_ecdsa_sk";

		// The passphrase KDF takes a moment, and the caller may be the renderer's thread.
		return await Task.Run(
			() =>
			{
				using PinnedBytes privateBlob = SecurityKeyFile.PrivateBlob(algorithm, publicKey, application, flags, credentialId);
				using PinnedBytes passphrase = PinnedBytes.FromString(request.Passphrase ?? "");
				return SshKeyGenerator.Build(fileName, publicBlob, privateBlob.Span, comment, passphrase.Span);
			},
			cancellationToken);
	}

	/// <summary>
	/// What OpenSSH's middleware asks for: a resident or verify-required key gets credential protection, so a lost
	/// device does not hand its resident keys to whoever finds it.
	/// </summary>
	public static SecurityKeyProtection ProtectionFor(bool residentKey, bool requireUserVerification) =>
		requireUserVerification ? SecurityKeyProtection.UserVerificationRequired
		: residentKey ? SecurityKeyProtection.UserVerificationOptionalWithCredentialIdList
		: SecurityKeyProtection.None;

	/// <summary>
	/// The credential id and public key from the attested credential data, after checking that the credential was made
	/// for this application and that the platform reported the same id.
	/// </summary>
	/// <exception cref="SecurityKeyException">The authenticator data is not what a new credential returns.</exception>
	public static (byte[] CredentialId, byte[] PublicKey) ReadCredential(SecurityKeyCredential credential, string application, SecurityKeyAlgorithm algorithm)
	{
		ArgumentNullException.ThrowIfNull(credential);
		ReadOnlySpan<byte> data = credential.AuthenticatorData;
		if (!AuthenticatorDataHeader.TryRead(data, application, out AuthenticatorDataHeader header)
			|| (header.Flags & AuthenticatorDataHeader.AttestedCredentialData) == 0)
		{
			throw Unreadable();
		}

		ReadOnlySpan<byte> attested = data[AuthenticatorDataHeader.Length..];
		if (attested.Length < AaguidLength + CredentialIdLengthSize)
		{
			throw Unreadable();
		}

		int idLength = BinaryPrimitives.ReadUInt16BigEndian(attested[AaguidLength..]);
		attested = attested[(AaguidLength + CredentialIdLengthSize)..];
		if (idLength is 0 or > SecurityKeyFile.MaxKeyHandleLength || attested.Length < idLength)
		{
			throw Unreadable();
		}

		ReadOnlySpan<byte> credentialId = attested[..idLength];
		if (credential.CredentialId.Length > 0 && !credentialId.SequenceEqual(credential.CredentialId))
		{
			throw Unreadable();
		}

		byte[] publicKey = CoseKey.ReadPublicKey(attested[idLength..], algorithm);
		return SecurityKeyFile.IsValidPublicKey(algorithm, publicKey)
			? (credentialId.ToArray(), publicKey)
			: throw Unreadable();
	}

	private static SecurityKeyException Unreadable() =>
		new(SecurityKeyFailure.Failed, "The security key answered with a credential that could not be read.");
}
