using Mokaterm.Core.Keys;

namespace Mokaterm.Core.Tests.Keys;

public sealed class AesCtrTests
{
	/// <summary>NIST SP 800-38A, F.5.5: CTR-AES256.Encrypt.</summary>
	[Fact]
	public void Transform_MatchesTheNistVector()
	{
		byte[] key = Convert.FromHexString("603DEB1015CA71BE2B73AEF0857D77811F352C073B6108D72D9810A30914DFF4");
		byte[] counter = Convert.FromHexString("F0F1F2F3F4F5F6F7F8F9FAFBFCFDFEFF");
		byte[] data = Convert.FromHexString("6BC1BEE22E409F96E93D7E117393172AAE2D8A571E03AC9C9EB76FAC45AF8E51");

		AesCtr.Transform(key, counter, data);

		Assert.Equal("601ec313775789a5b7a7f504bbf3d228f443e3ca4d62b59aca84e990cacaf5c5", Convert.ToHexStringLower(data));
	}

	[Fact]
	public void Transform_IsItsOwnInverseAndHandlesAPartialLastBlock()
	{
		byte[] key = new byte[32];
		byte[] counter = new byte[16];
		byte[] plain = [.. Enumerable.Range(0, 37).Select(value => (byte)value)];
		byte[] data = [.. plain];

		AesCtr.Transform(key, counter, data);
		Assert.NotEqual(plain, data);

		AesCtr.Transform(key, counter, data);
		Assert.Equal(plain, data);
	}

	[Fact]
	public void Transform_RefusesACounterThatIsNotOneBlock() =>
		Assert.Throws<ArgumentException>(() => AesCtr.Transform(new byte[32], new byte[8], new byte[16]));
}
