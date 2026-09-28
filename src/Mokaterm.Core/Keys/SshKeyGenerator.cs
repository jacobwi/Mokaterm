using System.Security.Cryptography;
using System.Text;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Core.Security;
using Org.BouncyCastle.Math.EC.Rfc8032;

namespace Mokaterm.Core.Keys;

/// <summary>
/// Creates key pairs in OpenSSH format. RSA comes from the framework; ed25519 comes from BouncyCastle because
/// .NET 10 has no Ed25519 API.
/// </summary>
internal sealed class SshKeyGenerator : ISshKeyGenerator
{
	private const string Ed25519Algorithm = "ssh-ed25519";
	private const string RsaAlgorithm = "ssh-rsa";

	public Task<GeneratedSshKey> GenerateAsync(SshKeyRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		if (!Enum.IsDefined(request.Type))
		{
			throw new ArgumentOutOfRangeException(nameof(request), request.Type, "Unknown key type.");
		}

		// RSA primes and the bcrypt KDF both take seconds; neither belongs on the renderer's thread.
		return Task.Run(() => Generate(request), cancellationToken);
	}

	public SshPublicKey? ReadPublicKey(string privateKey)
	{
		if (string.IsNullOrWhiteSpace(privateKey)
			|| !OpenSshPrivateKeyFile.TryReadPublicPart(privateKey, out byte[] blob, out string comment))
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
		SshWire.WriteString(publicWriter, Ed25519Algorithm);
		SshWire.WriteString(publicWriter, publicKey);

		// OpenSSH stores the seed and the public key together as the 64 byte "secret key".
		using PinnedBytes secret = new(Ed25519.SecretKeySize + Ed25519.PublicKeySize);
		seed.Span.CopyTo(secret.Span);
		publicKey.CopyTo(secret.Span[Ed25519.SecretKeySize..]);

		using PinnedBufferWriter privateWriter = new(256);
		SshWire.WriteString(privateWriter, Ed25519Algorithm);
		SshWire.WriteString(privateWriter, publicKey);
		SshWire.WriteString(privateWriter, secret.Span);

		return Build("id_ed25519", publicWriter.WrittenSpan, privateWriter.WrittenSpan, comment, passphrase);
	}

	private static GeneratedSshKey GenerateRsa(int bits, string comment, ReadOnlySpan<byte> passphrase)
	{
		using RSA rsa = RSA.Create(bits);
		RSAParameters parameters = rsa.ExportParameters(includePrivateParameters: true);
		try
		{
			using PinnedBufferWriter publicWriter = new(1024);
			SshWire.WriteString(publicWriter, RsaAlgorithm);
			SshWire.WriteMpInt(publicWriter, Field(parameters.Exponent));
			SshWire.WriteMpInt(publicWriter, Field(parameters.Modulus));

			// OpenSSH writes the private half as n, e, d, iqmp, p, q, which is not the order of the public half.
			using PinnedBufferWriter privateWriter = new(4096);
			SshWire.WriteString(privateWriter, RsaAlgorithm);
			SshWire.WriteMpInt(privateWriter, Field(parameters.Modulus));
			SshWire.WriteMpInt(privateWriter, Field(parameters.Exponent));
			SshWire.WriteMpInt(privateWriter, Field(parameters.D));
			SshWire.WriteMpInt(privateWriter, Field(parameters.InverseQ));
			SshWire.WriteMpInt(privateWriter, Field(parameters.P));
			SshWire.WriteMpInt(privateWriter, Field(parameters.Q));

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

	private static void Wipe(byte[]? value)
	{
		if (value is not null)
		{
			CryptographicOperations.ZeroMemory(value);
		}
	}

	private static GeneratedSshKey Build(
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

	private static SshPublicKey Describe(ReadOnlySpan<byte> publicBlob, string comment)
	{
		SshWireReader reader = new(publicBlob);
		string algorithm = reader.TryReadString(out ReadOnlySpan<byte> name) ? Encoding.UTF8.GetString(name) : "";
		string encoded = Convert.ToBase64String(publicBlob);
		string line = comment.Length == 0 ? $"{algorithm} {encoded}" : $"{algorithm} {encoded} {comment}";
		string fingerprint = "SHA256:" + Convert.ToBase64String(SHA256.HashData(publicBlob)).TrimEnd('=');
		return new SshPublicKey(algorithm, comment, line, fingerprint);
	}
}
