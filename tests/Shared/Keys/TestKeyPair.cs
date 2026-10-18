using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Math.EC.Rfc8032;

namespace Mokaterm.Tests.Shared;

/// <summary>
/// A fresh key pair with its public blob and its private half the way PuTTY keeps it: RSA as d, p, q, iqmp; ECDSA as the
/// private scalar; Ed25519 as the 32 byte seed in little endian order with its top zero bytes left off.
/// </summary>
internal sealed record TestKeyPair(string Algorithm, byte[] PublicBlob, byte[] PuttyPrivateBlob)
{
	/// <summary>The <c>SHA256:</c> fingerprint OpenSSH prints for the public blob.</summary>
	public string Fingerprint => "SHA256:" + Convert.ToBase64String(SHA256.HashData(PublicBlob)).TrimEnd('=');

	/// <summary>The public key as one <c>authorized_keys</c> line without a comment.</summary>
	public string PublicLine => $"{Algorithm} {Convert.ToBase64String(PublicBlob)}";

	public static TestKeyPair Rsa(int bits = 2048)
	{
		using RSA rsa = RSA.Create(bits);
		RSAParameters p = rsa.ExportParameters(includePrivateParameters: true);
		byte[] publicBlob = [.. Ssh("ssh-rsa"), .. PuttyKeyWriter.MpInt(p.Exponent), .. PuttyKeyWriter.MpInt(p.Modulus)];
		byte[] privateBlob = [.. PuttyKeyWriter.MpInt(p.D), .. PuttyKeyWriter.MpInt(p.P), .. PuttyKeyWriter.MpInt(p.Q), .. PuttyKeyWriter.MpInt(p.InverseQ)];
		return new TestKeyPair("ssh-rsa", publicBlob, privateBlob);
	}

	/// <param name="seed">32 bytes, or null for random ones.</param>
	public static TestKeyPair Ed25519Key(byte[]? seed = null)
	{
		seed ??= RandomNumberGenerator.GetBytes(32);
		byte[] publicKey = new byte[Ed25519.PublicKeySize];
		Ed25519.GeneratePublicKey(seed, 0, publicKey, 0);
		byte[] publicBlob = [.. Ssh("ssh-ed25519"), .. PuttyKeyWriter.String(publicKey)];

		// The seed read as a little endian number, written with only as many bytes as the number needs.
		int length = seed.Length;
		while (length > 1 && seed[length - 1] == 0)
		{
			length--;
		}

		return new TestKeyPair("ssh-ed25519", publicBlob, PuttyKeyWriter.String(seed.AsSpan(0, length)));
	}

	/// <param name="curve"><c>nistp256</c>, <c>nistp384</c> or <c>nistp521</c>.</param>
	public static TestKeyPair Ecdsa(string curve)
	{
		ECCurve named = curve switch
		{
			"nistp256" => ECCurve.NamedCurves.nistP256,
			"nistp384" => ECCurve.NamedCurves.nistP384,
			_ => ECCurve.NamedCurves.nistP521,
		};

		using ECDsa ecdsa = ECDsa.Create(named);
		ECParameters p = ecdsa.ExportParameters(includePrivateParameters: true);
		string algorithm = "ecdsa-sha2-" + curve;
		byte[] point = [0x04, .. p.Q.X!, .. p.Q.Y!];
		byte[] publicBlob = [.. Ssh(algorithm), .. Ssh(curve), .. PuttyKeyWriter.String(point)];
		return new TestKeyPair(algorithm, publicBlob, PuttyKeyWriter.MpInt(p.D));
	}

	private static byte[] Ssh(string value) => PuttyKeyWriter.String(Encoding.ASCII.GetBytes(value));
}
