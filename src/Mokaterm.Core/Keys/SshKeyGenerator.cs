using System.Security.Cryptography;
using System.Text;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Security;
using Mokaterm.Core.Security;
using Org.BouncyCastle.Math.EC.Rfc8032;

namespace Mokaterm.Core.Keys;

/// <summary>
/// Creates key pairs in OpenSSH format. RSA comes from the framework; ed25519 comes from BouncyCastle because
/// .NET 10 has no Ed25519 API. Security keys come from the host's authenticator, when it has one.
/// </summary>
internal sealed class SshKeyGenerator : ISshKeyGenerator
{
	private readonly ISecurityKeyAuthenticator? _authenticator;

	/// <param name="authenticator">The desktop host's security key access; null where there is none.</param>
	public SshKeyGenerator(ISecurityKeyAuthenticator? authenticator = null) => _authenticator = authenticator;

	public Task<GeneratedSshKey> GenerateAsync(SshKeyRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		if (!Enum.IsDefined(request.Type))
		{
			throw new ArgumentOutOfRangeException(nameof(request), request.Type, "Unknown key type.");
		}

		if (request.IsSecurityKey)
		{
			return SecurityKeyEnrollment.GenerateAsync(_authenticator, request, CleanComment(request.Comment), cancellationToken);
		}

		// RSA primes and the bcrypt KDF both take seconds; neither belongs on the renderer's thread.
		return Task.Run(() => Generate(request), cancellationToken);
	}

	public SshPublicKey? ReadPublicKey(string privateKey)
	{
		if (string.IsNullOrWhiteSpace(privateKey) || !KeyFilePublicHalf.TryRead(privateKey, out byte[] blob, out string comment))
		{
			return null;
		}

		return Describe(blob, comment);
	}

	/// <summary>A comment lives on one line at the end of the public key, so anything line breaking is folded out.</summary>
	internal static string CleanComment(string? comment)
	{
		if (string.IsNullOrWhiteSpace(comment))
		{
			return "";
		}

		StringBuilder builder = new(comment.Length);
		foreach (char character in comment)
		{
			builder.Append(char.IsControl(character) ? ' ' : character);
		}

		return builder.ToString().Trim();
	}

	private static GeneratedSshKey Generate(SshKeyRequest request)
	{
		string comment = CleanComment(request.Comment);
		using PinnedBytes passphrase = PinnedBytes.FromString(request.Passphrase ?? "");
		return request.Type switch
		{
			SshKeyType.Ed25519 => GenerateEd25519(comment, passphrase.Span),
			SshKeyType.Rsa3072 => GenerateRsa(3072, comment, passphrase.Span),
			_ => GenerateRsa(4096, comment, passphrase.Span),
		};
	}

	private static GeneratedSshKey GenerateEd25519(string comment, ReadOnlySpan<byte> passphrase)
	{
		using PinnedBytes seed = new(Ed25519.SecretKeySize);
		RandomNumberGenerator.Fill(seed.Span);
		byte[] publicKey = new byte[Ed25519.PublicKeySize];
		Ed25519.GeneratePublicKey(seed.Array, 0, publicKey, 0);

		using PinnedBufferWriter publicWriter = new(128);
		SshKeyBlobs.WriteEd25519Public(publicWriter, publicKey);

		using PinnedBufferWriter privateWriter = new(256);
		SshKeyBlobs.WriteEd25519Private(privateWriter, publicKey, seed.Span);

		return Build("id_ed25519", publicWriter.WrittenSpan, privateWriter.WrittenSpan, comment, passphrase);
	}

	private static GeneratedSshKey GenerateRsa(int bits, string comment, ReadOnlySpan<byte> passphrase)
	{
		using RSA rsa = RSA.Create(bits);
		RSAParameters parameters = rsa.ExportParameters(includePrivateParameters: true);
		try
		{
			using PinnedBufferWriter publicWriter = new(1024);
			SshKeyBlobs.WriteRsaPublic(publicWriter, Field(parameters.Exponent), Field(parameters.Modulus));

			using PinnedBufferWriter privateWriter = new(4096);
			SshKeyBlobs.WriteRsaPrivate(
				privateWriter,
				Field(parameters.Modulus),
				Field(parameters.Exponent),
				Field(parameters.D),
				Field(parameters.InverseQ),
				Field(parameters.P),
				Field(parameters.Q));

			return Build("id_rsa", publicWriter.WrittenSpan, privateWriter.WrittenSpan, comment, passphrase);
		}
		finally
		{
			Wipe(parameters.D);
			Wipe(parameters.P);
			Wipe(parameters.Q);
			Wipe(parameters.DP);
			Wipe(parameters.DQ);
			Wipe(parameters.InverseQ);
		}
	}

	private static byte[] Field(byte[]? value) =>
		value ?? throw new CryptographicException("The generated key is missing a parameter.");

	internal static void Wipe(byte[]? value)
	{
		if (value is not null)
		{
			CryptographicOperations.ZeroMemory(value);
		}
	}

	internal static GeneratedSshKey Build(
		string fileName,
		ReadOnlySpan<byte> publicBlob,
		ReadOnlySpan<byte> privateBlob,
		string comment,
		ReadOnlySpan<byte> passphrase) => new()
		{
			PrivateKey = OpenSshPrivateKeyFile.Write(publicBlob, privateBlob, comment, passphrase),
			PublicKey = Describe(publicBlob, comment),
			HasPassphrase = !passphrase.IsEmpty,
			SuggestedFileName = fileName,
		};

	/// <summary>The public half as the keychain shows it: algorithm, <c>authorized_keys</c> line and the OpenSSH fingerprint.</summary>
	internal static SshPublicKey Describe(ReadOnlySpan<byte> publicBlob, string comment)
	{
		SshWireReader reader = new(publicBlob);
		string algorithm = reader.TryReadString(out ReadOnlySpan<byte> name) ? Encoding.UTF8.GetString(name) : "";
		string encoded = Convert.ToBase64String(publicBlob);
		string line = comment.Length == 0 ? $"{algorithm} {encoded}" : $"{algorithm} {encoded} {comment}";
		string fingerprint = "SHA256:" + Convert.ToBase64String(SHA256.HashData(publicBlob)).TrimEnd('=');
		return new SshPublicKey(algorithm, comment, line, fingerprint);
	}
}
