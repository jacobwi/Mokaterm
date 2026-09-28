using System.Text;
using Mokaterm.Core.Keys;

namespace Mokaterm.Core.Tests.Keys;

public sealed class BcryptPbkdfTests
{
	[Fact]
	public void Blowfish_MatchesTheStandardAllZeroTestVector()
	{
		Blowfish blowfish = new();
		blowfish.Expand(new byte[8], default);

		uint left = 0;
		uint right = 0;
		blowfish.Encipher(ref left, ref right);

		Assert.Equal(0x4EF99745u, left);
		Assert.Equal(0x6198DD78u, right);
	}

	/// <summary>
	/// The vector every bcrypt_pbkdf port checks itself against, taken from OpenBSD's implementation. It also
	/// proves the Blowfish state derived from the digits of pi is right.
	/// </summary>
	[Fact]
	public void DeriveKey_MatchesTheOpenBsdTestVector()
	{
		byte[] key = new byte[32];

		BcryptPbkdf.DeriveKey("password"u8, "salt"u8, rounds: 4, key);

		Assert.Equal(
			"5bbf0cc293587f1c3635555c27796598d47e579071bf427e9d8fbe842aba34d9",
			Convert.ToHexStringLower(key));
	}

	[Fact]
	public void DeriveKey_SpreadsOutputAcrossMoreThanOneBlock()
	{
		byte[] key = new byte[48];

		BcryptPbkdf.DeriveKey(
			Encoding.UTF8.GetBytes("pleaseletmein"),
			Encoding.UTF8.GetBytes("saltSALTsalt"),
			rounds: 8,
			key);

		Assert.Equal(
			"e4fccb5ea673c95d04a7f141d1a990c6e406e5a8fa67f88fc66200dd3728ce6164dda652e14fd1d4fde83a0071fb2561",
			Convert.ToHexStringLower(key));
	}

	[Fact]
	public void DeriveKey_RefusesZeroRounds() =>
		Assert.Throws<ArgumentOutOfRangeException>(() => BcryptPbkdf.DeriveKey("p"u8, "s"u8, rounds: 0, new byte[32]));
}
