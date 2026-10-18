using System.Text;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Core.Keys;

namespace Mokaterm.Core.Tests.Keys;

/// <summary>
/// PuTTY key files, formats 2 and 3, with and without a passphrase. The files come from <see cref="PuttyKeyWriter"/>,
/// which follows PuTTY's published format: there is no puttygen here, so these check the reader against our reading of
/// the specification. The converted keys are then checked against OpenSSH's own ssh-keygen, which signs with them.
/// </summary>
public sealed class PuttyKeyFileTests
{
	private const string Passphrase = "correct horse";
	private const string Comment = "imported-key-20261018";

	public static TheoryData<int, string, bool> Files
	{
		get
		{
			TheoryData<int, string, bool> data = [];
			foreach (int version in new[] { 2, 3 })
			{
				foreach (string type in new[] { "rsa", "ed25519", "nistp256", "nistp384", "nistp521" })
				{
					data.Add(version, type, false);
					data.Add(version, type, true);
				}
			}

			return data;
		}
	}

	[Theory]
	[MemberData(nameof(Files))]
	public void TryConvert_OpensTheFile_AndWritesTheSameKeyInOpenSshForm(int version, string type, bool encrypted)
	{
		TestKeyPair key = Key(type);
		string text = PuttyKeyWriter.Write(version, key, Comment, encrypted ? Passphrase : null);

		PrivateKeyStatus status = PuttyKeyFile.TryConvert(Bytes(text), encrypted ? Passphrase : null, out string? openSsh, out byte[] publicBlob, out string? error);

		Assert.True(status == PrivateKeyStatus.Valid, error);
		Assert.Equal(key.PublicBlob, publicBlob);
		ParsedOpenSshKey parsed = OpenSshKeyParser.Parse(openSsh!);
		Assert.Equal("none", parsed.Cipher);
		Assert.Equal(key.PublicBlob, parsed.PublicBlob);
		(string keyType, List<byte[]> fields, string comment) = OpenSshKeyParser.ReadSection(parsed.Section);
		Assert.Equal(key.Algorithm, keyType);
		Assert.Equal(Comment, comment);
		Assert.Equal(type == "rsa" ? 6 : type == "ed25519" ? 2 : 3, fields.Count);
	}

	[Fact]
	public void TryConvert_ReadsAnEd25519SeedPuttyWroteWithoutItsTopZeroByte()
	{
		byte[] seed = new byte[32];
		new Random(7).NextBytes(seed);
		seed[31] = 0;
		TestKeyPair key = TestKeyPair.Ed25519Key(seed);
		Assert.Equal(4 + 31, key.PuttyPrivateBlob.Length);

		PrivateKeyStatus status = PuttyKeyFile.TryConvert(Bytes(PuttyKeyWriter.Write(3, key, Comment, null)), null, out string? openSsh, out _, out string? error);

		Assert.True(status == PrivateKeyStatus.Valid, error);
		(_, List<byte[]> fields, _) = OpenSshKeyParser.ReadSection(OpenSshKeyParser.Parse(openSsh!).Section);
		Assert.Equal(seed, fields[1][..32]);
	}

	[Theory]
	[InlineData(2)]
	[InlineData(3)]
	public void TryConvert_WithTheWrongPassphrase_SaysSo(int version)
	{
		string text = PuttyKeyWriter.Write(version, TestKeyPair.Ed25519Key(), Comment, Passphrase);

		Assert.Equal(PrivateKeyStatus.WrongPassphrase, PuttyKeyFile.TryConvert(Bytes(text), "not it", out string? openSsh, out _, out _));
		Assert.Null(openSsh);
		Assert.Equal(PrivateKeyStatus.PassphraseRequired, PuttyKeyFile.TryConvert(Bytes(text), null, out _, out _, out _));
	}

	[Theory]
	[InlineData("Argon2i")]
	[InlineData("Argon2d")]
	public void TryConvert_ReadsEveryArgon2Flavour(string flavour)
	{
		TestKeyPair key = TestKeyPair.Ecdsa("nistp256");
		string text = PuttyKeyWriter.Write(3, key, Comment, Passphrase, new PuttyKeyWriter.Argon2Settings(flavour, MemoryKiB: 256, Passes: 2, Parallelism: 2));

		Assert.Equal(PrivateKeyStatus.Valid, PuttyKeyFile.TryConvert(Bytes(text), Passphrase, out _, out byte[] blob, out _));
		Assert.Equal(key.PublicBlob, blob);
	}

	[Theory]
	[InlineData(2)]
	[InlineData(3)]
	public void TryConvert_RefusesAFileWhoseMacDoesNotMatch(int version)
	{
		TestKeyPair key = TestKeyPair.Ed25519Key();
		string text = PuttyKeyWriter.Write(version, key, Comment, null, tamper: blob => blob[^1] ^= 1);

		PrivateKeyStatus status = PuttyKeyFile.TryConvert(Bytes(text), null, out string? openSsh, out _, out string? error);

		Assert.Equal(PrivateKeyStatus.Invalid, status);
		Assert.Null(openSsh);
		Assert.Contains("MAC", error, StringComparison.Ordinal);
	}

	[Fact]
	public void TryConvert_RefusesAChangedComment_WhichTheMacCovers()
	{
		string text = PuttyKeyWriter.Write(3, TestKeyPair.Ed25519Key(), Comment, null).Replace(Comment, "someone else", StringComparison.Ordinal);

		Assert.Equal(PrivateKeyStatus.Invalid, PuttyKeyFile.TryConvert(Bytes(text), null, out _, out _, out _));
	}

	[Fact]
	public void TryConvert_RefusesAPrivateHalfThatBelongsToAnotherKey_EvenWithAGoodMac()
	{
		TestKeyPair key = TestKeyPair.Ed25519Key();
		TestKeyPair other = TestKeyPair.Ed25519Key();
		string text = PuttyKeyWriter.Write(3, key, Comment, null, privateBlob: other.PuttyPrivateBlob);

		Assert.Equal(PrivateKeyStatus.Invalid, PuttyKeyFile.TryConvert(Bytes(text), null, out string? openSsh, out _, out _));
		Assert.Null(openSsh);
	}

	[Fact]
	public void TryConvert_RefusesAnRsaKeyWhosePrimesDoNotMakeItsModulus()
	{
		TestKeyPair key = TestKeyPair.Rsa();
		TestKeyPair other = TestKeyPair.Rsa();
		string text = PuttyKeyWriter.Write(2, key, Comment, null, privateBlob: other.PuttyPrivateBlob);

		Assert.Equal(PrivateKeyStatus.Invalid, PuttyKeyFile.TryConvert(Bytes(text), null, out _, out _, out _));
	}

	[Fact]
	public void TryReadPublicPart_ReadsAProtectedFileWithoutItsPassphrase()
	{
		TestKeyPair key = TestKeyPair.Rsa();
		string text = PuttyKeyWriter.Write(3, key, Comment, Passphrase, lineEnding: "\n");

		Assert.Equal(PrivateKeyStatus.Valid, PuttyKeyFile.TryReadPublicPart(Bytes(text), out PuttyPublicPart? part, out _));
		Assert.NotNull(part);
		Assert.Equal("ssh-rsa", part.Algorithm);
		Assert.Equal(Comment, part.Comment);
		Assert.True(part.IsEncrypted);
		Assert.Equal(key.PublicBlob, part.PublicBlob);
	}

	[Fact]
	public void ReadPublicKey_GivesTheKeychainThePublicHalfOfAPuttyFile()
	{
		TestKeyPair key = TestKeyPair.Ed25519Key();

		SshPublicKey? publicKey = new SshKeyGenerator().ReadPublicKey(PuttyKeyWriter.Write(3, key, Comment, Passphrase));

		Assert.NotNull(publicKey);
		Assert.Equal($"{key.PublicLine} {Comment}", publicKey.AuthorizedKeysLine);
		Assert.Equal(key.Fingerprint, publicKey.Fingerprint);
	}

	[Theory]
	[InlineData("PuTTY-User-Key-File-1: ssh-rsa", PrivateKeyStatus.Unsupported)]
	[InlineData("PuTTY-User-Key-File-4: ssh-rsa", PrivateKeyStatus.Unsupported)]
	[InlineData("PuTTY-User-Key-File-3 ssh-rsa", PrivateKeyStatus.Invalid)]
	public void TryReadPublicPart_RefusesOtherFormatsAndBrokenHeaders(string header, PrivateKeyStatus expected)
	{
		string text = PuttyKeyWriter.Write(3, TestKeyPair.Rsa(), Comment, null);
		text = header + text[text.IndexOf('\r', StringComparison.Ordinal)..];

		Assert.Equal(expected, PuttyKeyFile.TryReadPublicPart(Bytes(text), out PuttyPublicPart? part, out _));
		Assert.Null(part);
	}

	[Theory]
	[InlineData("Encryption: none", "Encryption: des3-cbc", PrivateKeyStatus.Unsupported)]
	[InlineData("Public-Lines: ", "Public-Lines: 9999", PrivateKeyStatus.Invalid)]
	[InlineData("Private-MAC: ", "Private-MAC: zz", PrivateKeyStatus.Invalid)]
	public void TryReadPublicPart_RefusesDamagedFiles(string find, string replace, PrivateKeyStatus expected)
	{
		string text = PuttyKeyWriter.Write(3, TestKeyPair.Ed25519Key(), Comment, null);
		int at = text.IndexOf(find, StringComparison.Ordinal);
		int end = text.IndexOf('\r', at);
		text = text[..at] + replace + text[end..];

		Assert.Equal(expected, PuttyKeyFile.TryReadPublicPart(Bytes(text), out _, out _));
	}

	[Fact]
	public void TryConvert_RefusesArgon2SettingsThatWouldTakeMinutes()
	{
		string text = PuttyKeyWriter.Write(3, TestKeyPair.Ed25519Key(), Comment, Passphrase);
		int at = text.IndexOf("Argon2-Memory: ", StringComparison.Ordinal);
		int end = text.IndexOf('\r', at);
		text = text[..at] + "Argon2-Memory: " + (PuttyKeyFile.MaxArgon2MemoryKiB + 1) + text[end..];

		PrivateKeyStatus status = PuttyKeyFile.TryConvert(Bytes(text), Passphrase, out _, out _, out string? error);

		Assert.Equal(PrivateKeyStatus.Unsupported, status);
		Assert.Contains("Argon2", error, StringComparison.Ordinal);
	}

	[Theory(Timeout = TestTimeouts.NetworkMilliseconds)]
	[InlineData(2, "rsa")]
	[InlineData(3, "ed25519")]
	[InlineData(3, "nistp256")]
	[InlineData(2, "nistp521")]
	public async Task SshKeygen_SignsWithAConvertedKey_AndPrintsItsFingerprint(int version, string type)
	{
		CancellationToken ct = TestContext.Current.CancellationToken;
		string keygen = OpenSshKeygen.Find() ?? "";
		Assert.SkipWhen(keygen.Length == 0, "No ssh-keygen is installed here to check the key against.");
		TestKeyPair key = Key(type);
		Assert.Equal(PrivateKeyStatus.Valid, PuttyKeyFile.TryConvert(Bytes(PuttyKeyWriter.Write(version, key, Comment, Passphrase)), Passphrase, out string? openSsh, out _, out _));

		await using TempFolder folder = new();
		string path = Path.Combine(folder.Path, "converted");
		string message = Path.Combine(folder.Path, "message");
		await File.WriteAllTextAsync(path, openSsh, ct);
		await File.WriteAllTextAsync(message, "signed by a key that came from PuTTY", ct);

		(int exitCode, string output, string error) = await OpenSshKeygen.RunAsync(keygen, ["-y", "-f", path], null, ct);
		Assert.True(exitCode == 0, error);
		Assert.Equal($"{key.PublicLine} {Comment}", output.Trim());

		(exitCode, output, error) = await OpenSshKeygen.RunAsync(keygen, ["-l", "-f", path], null, ct);
		Assert.True(exitCode == 0, error);
		Assert.Contains(key.Fingerprint, output, StringComparison.Ordinal);

		// A signature OpenSSH makes with the private half and then accepts is the proof that half was converted right.
		(exitCode, _, error) = await OpenSshKeygen.RunAsync(keygen, ["-Y", "sign", "-f", path, "-n", "mokaterm", message], null, ct);
		Assert.True(exitCode == 0, error);
		(exitCode, output, error) = await OpenSshKeygen.RunAsync(
			keygen,
			["-Y", "check-novalidate", "-n", "mokaterm", "-s", message + ".sig"],
			await File.ReadAllBytesAsync(message, ct),
			ct);
		Assert.True(exitCode == 0, error + output);
		Assert.Contains(key.Fingerprint, output + error, StringComparison.Ordinal);
	}

	private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

	private static TestKeyPair Key(string type) => type switch
	{
		"rsa" => TestKeyPair.Rsa(),
		"ed25519" => TestKeyPair.Ed25519Key(),
		_ => TestKeyPair.Ecdsa(type),
	};
}
