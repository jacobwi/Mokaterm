using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Core.Extensions;
using Mokaterm.Modules.Ssh.Keys;
using Renci.SshNet;

namespace Mokaterm.Modules.Ssh.Tests;

/// <summary>
/// The PuTTY files the tests write, opened by SSH.NET's own PuTTY reader: a second reading of PuTTY's published format,
/// made independently of ours. That reader is also what opens an imported .ppk kept with its passphrase, at connect,
/// and what the keychain's inspector asks, so a file it takes is a file a login can use.
/// </summary>
public sealed class PuttyKeyCrossCheckTests
{
	private const string Passphrase = "correct horse";

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
	public void SshNet_OpensTheFile_AsTheKeyItClaimsToHold(int version, string type, bool encrypted)
	{
		TestKeyPair key = Key(type);
		string text = PuttyKeyWriter.Write(version, key, "cross-check", encrypted ? Passphrase : null);

		using PrivateKeyFile file = Open(text, encrypted ? Passphrase : null);

		Assert.Equal(key.PublicBlob, file.HostKeyAlgorithms.First().Data);
	}

	[Theory]
	[InlineData(2)]
	[InlineData(3)]
	public void SshNet_RefusesTheWrongPassphrase(int version)
	{
		string text = PuttyKeyWriter.Write(version, Key("ed25519"), "cross-check", Passphrase);

		_ = Assert.ThrowsAny<Exception>(() => Open(text, "not it").Dispose());
	}

	[Theory]
	[InlineData(2)]
	[InlineData(3)]
	public void SshNet_RefusesAFileWhoseMacDoesNotMatch(int version)
	{
		string text = PuttyKeyWriter.Write(version, Key("nistp256"), "cross-check", null, tamper: blob => blob[^1] ^= 1);

		_ = Assert.ThrowsAny<Exception>(() => Open(text, null).Dispose());
	}

	[Theory]
	[InlineData(2, "rsa")]
	[InlineData(3, "ed25519")]
	[InlineData(3, "nistp384")]
	public void TheKeychain_NamesALockedFileTheWaySshNetDoesOnceItIsOpen(int version, string type)
	{
		string text = PuttyKeyWriter.Write(version, Key(type), "cross-check", Passphrase);
		SshPrivateKeyInspector inspector = new();
		using ServiceProvider core = new ServiceCollection().AddMokatermCore().BuildServiceProvider();

		// What the store records for a key kept locked: Core reads the public half, and nothing is decrypted.
		SshPublicKey? locked = core.GetRequiredService<ISshKeyGenerator>().ReadPublicKey(text);
		PrivateKeyInspection opened = inspector.Inspect(text, Passphrase);

		Assert.Equal(PrivateKeyStatus.PassphraseRequired, inspector.Inspect(text, null).Status);
		Assert.Equal(PrivateKeyStatus.Valid, opened.Status);
		Assert.NotNull(locked);
		Assert.Equal(opened.Fingerprint, locked.Fingerprint);
		Assert.Equal(opened.Algorithm, locked.Algorithm);
	}

	private static PrivateKeyFile Open(string text, string? passphrase)
	{
		using MemoryStream stream = new(Encoding.ASCII.GetBytes(text));
		return new PrivateKeyFile(stream, passphrase);
	}

	private static TestKeyPair Key(string type)
	{
		switch (type)
		{
			case "rsa":
				return TestKeyPair.Rsa();
			case "ed25519":
				// A seed whose top byte is zero is stored a byte short, which is PuTTY's business; this one stays whole.
				byte[] seed = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
				seed[31] |= 1;
				return TestKeyPair.Ed25519Key(seed);
			default:
				return TestKeyPair.Ecdsa(type);
		}
	}
}
