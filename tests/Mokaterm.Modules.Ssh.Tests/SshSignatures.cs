using System.Security.Cryptography;
using System.Text;
using Mokaterm.Modules.Ssh.Agent;

namespace Mokaterm.Modules.Ssh.Tests;

/// <summary>One SSHSIG signature, the format <c>ssh-keygen -Y sign</c> writes (PROTOCOL.sshsig).</summary>
internal sealed record SshSignature(byte[] PublicKey, string Namespace, string HashAlgorithm, byte[] Signature);

/// <summary>
/// SSHSIG files, which carry a key's raw signature over a fixed wrapper of the message. They are the one place where
/// OpenSSH publishes real security key signatures, WebAuthn ones included, and <c>ssh-keygen -Y check-novalidate</c>
/// checks one with the same <c>sshkey_verify</c> sshd calls, which is what makes them useful here.
/// </summary>
internal static class SshSignatures
{
	private const string Begin = "-----BEGIN SSH SIGNATURE-----";
	private const string End = "-----END SSH SIGNATURE-----";

	/// <summary>The message OpenSSH's sshsig tests sign (regress/unittests/sshsig/testdata/signed-data).</summary>
	public static readonly byte[] TestMessage = Encoding.ASCII.GetBytes("This is a test, this is only a test");

	public const string TestNamespace = "unittest";

	/// <summary>A signature made by a browser through WebAuthn (regress/unittests/sshsig/testdata/ecdsa_sk_webauthn.sig).</summary>
	public const string WebAuthnEcdsa = """
		-----BEGIN SSH SIGNATURE-----
		U1NIU0lHAAAAAQAAAIYAAAAic2stZWNkc2Etc2hhMi1uaXN0cDI1NkBvcGVuc3NoLmNvbQ
		AAAAhuaXN0cDI1NgAAAEEEFEbAOOzgeEkVyfi0nkavvYpyZGYJLU6KsRPFpe+ew0CmCg4s
		XVr/uvGV19Iwebvd5PWeEd73tbyqJw88QAoMlAAAAAttaW5kcm90Lm9yZwAAAAh1bml0dG
		VzdAAAAAAAAAAGc2hhNTEyAAABhwAAACt3ZWJhdXRobi1zay1lY2RzYS1zaGEyLW5pc3Rw
		MjU2QG9wZW5zc2guY29tAAAASQAAACBj2oMT9tb5wRXe6mdmf4/lgAO8wrgr95ouozwNg4
		itnQAAACEAtU9g5wz3HchUiLfLD6plr9T4TiJ32lVCrATSjpiy0SMBAAADHwAAABdodHRw
		czovL3d3dy5taW5kcm90Lm9yZwAAAON7InR5cGUiOiJ3ZWJhdXRobi5nZXQiLCJjaGFsbG
		VuZ2UiOiJVMU5JVTBsSEFBQUFDSFZ1YVhSMFpYTjBBQUFBQUFBQUFBWnphR0UxTVRJQUFB
		QkFMTHU4WmdjU3h0Nk1zRlV6dWlaZ0c2R3dNZEo5ZDd4ZUU3WW9SSXcwZzlpSEpfd3NGRD
		cxbzRXbHllenZGV0VqYnFRMHFDN0Z3R3Bqa2pVUVAtTmQ2dyIsIm9yaWdpbiI6Imh0dHBz
		Oi8vd3d3Lm1pbmRyb3Qub3JnIiwiY3Jvc3NPcmlnaW4iOmZhbHNlfQAAAAA=
		-----END SSH SIGNATURE-----
		""";

	/// <summary>A plain ECDSA security key signature (regress/unittests/sshsig/testdata/ecdsa_sk.sig).</summary>
	public const string Ecdsa = """
		-----BEGIN SSH SIGNATURE-----
		U1NIU0lHAAAAAQAAAH8AAAAic2stZWNkc2Etc2hhMi1uaXN0cDI1NkBvcGVuc3NoLmNvbQ
		AAAAhuaXN0cDI1NgAAAEEEoNVrmNFxPlXsTrKybBWLsclaHunkP6F5ss9qRBymcPZqMlno
		b/hbO6GVhadyVZjrcmykOnb31GWGrkBPzr+DwAAAAARzc2g6AAAACHVuaXR0ZXN0AAAAAA
		AAAAZzaGE1MTIAAAB3AAAAInNrLWVjZHNhLXNoYTItbmlzdHAyNTZAb3BlbnNzaC5jb20A
		AABIAAAAIHohGwyy8iKT3zwd1TYA9V/Ioo7h/3zCJUtyq/Qigt/HAAAAIGzidTwq7D/kFa
		7Xjcp/KkdbIs4MfQpfAW/0OciajlpzARI0Vng=
		-----END SSH SIGNATURE-----
		""";

	/// <summary>An Ed25519 security key signature (regress/unittests/sshsig/testdata/ed25519_sk.sig).</summary>
	public const string Ed25519 = """
		-----BEGIN SSH SIGNATURE-----
		U1NIU0lHAAAAAQAAAEoAAAAac2stc3NoLWVkMjU1MTlAb3BlbnNzaC5jb20AAAAgmxoNhd
		Biu5zpuKUJgMgrhhI54b7OatE7Yz23Tf//sscAAAAEc3NoOgAAAAh1bml0dGVzdAAAAAAA
		AAAGc2hhNTEyAAAAZwAAABpzay1zc2gtZWQyNTUxOUBvcGVuc3NoLmNvbQAAAEAi+7eTjW
		/+LQ2M+sCD+KFtH1n7VFFJon/SZFsxODyV8cWTlFKj617Ys1Ur5TV6uaEXQhck8rBA2oQI
		HTPANLIPARI0Vng=
		-----END SSH SIGNATURE-----
		""";

	public static SshSignature Parse(string armored)
	{
		string body = armored[(armored.IndexOf(Begin, StringComparison.Ordinal) + Begin.Length)..armored.IndexOf(End, StringComparison.Ordinal)];
		SshWireReader reader = new(Convert.FromBase64String(body));
		Assert.True(reader.TryReadByte(out byte s) && s == (byte)'S');
		Assert.True(reader.TryReadByte(out _) && reader.TryReadByte(out _) && reader.TryReadByte(out _) && reader.TryReadByte(out _) && reader.TryReadByte(out _));
		Assert.True(reader.TryReadUInt32(out uint version));
		Assert.Equal(1u, version);
		Assert.True(reader.TryReadString(out byte[] publicKey));
		Assert.True(reader.TryReadText(out string space));
		Assert.True(reader.TryReadString(out _));
		Assert.True(reader.TryReadText(out string hash));
		Assert.True(reader.TryReadString(out byte[] signature));
		Assert.Equal(0, reader.Remaining);
		return new SshSignature(publicKey, space, hash, signature);
	}

	/// <summary>What the key signs for <paramref name="message"/>: the preamble, namespace, reserved, hash name and hash.</summary>
	public static byte[] Signed(string space, byte[] message) =>
		[
			.. "SSHSIG"u8,
			.. new SshWireWriter()
				.WriteString(Encoding.UTF8.GetBytes(space))
				.WriteString([])
				.WriteString("sha512"u8)
				.WriteString(SHA512.HashData(message))
				.ToArray(),
		];

	/// <summary>An armored SSHSIG file around <paramref name="signature"/>, as <c>ssh-keygen -Y sign</c> would write it.</summary>
	public static string Armor(byte[] publicKey, string space, byte[] signature)
	{
		byte[] blob =
		[
			.. "SSHSIG"u8,
			.. new SshWireWriter()
				.WriteUInt32(1)
				.WriteString(publicKey)
				.WriteString(Encoding.UTF8.GetBytes(space))
				.WriteString([])
				.WriteString("sha512"u8)
				.WriteString(signature)
				.ToArray(),
		];

		string base64 = Convert.ToBase64String(blob);
		StringBuilder text = new();
		text.Append(Begin).Append('\n');
		for (int offset = 0; offset < base64.Length; offset += 70)
		{
			text.Append(base64, offset, Math.Min(70, base64.Length - offset)).Append('\n');
		}

		return text.Append(End).Append('\n').ToString();
	}
}
