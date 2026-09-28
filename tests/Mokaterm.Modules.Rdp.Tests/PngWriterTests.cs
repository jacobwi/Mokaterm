using Mokaterm.Modules.Rdp.Protocol;

namespace Mokaterm.Modules.Rdp.Tests;

public sealed class PngWriterTests
{
	[Fact]
	public void Encode_WritesTheSignatureAndTheThreeChunksInOrder()
	{
		byte[] png = PngWriter.Encode(RdpHarness.Gradient(4, 3), 4, 3);

		Assert.Equal<byte>([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], png.AsSpan(0, 8).ToArray());
		PngPicture picture = PngReader.Read(png);
		Assert.Equal(["IHDR", "IDAT", "IEND"], picture.Chunks);
	}

	[Fact]
	public void Encode_KeepsEveryPixel()
	{
		byte[] pixels = RdpHarness.Gradient(17, 9);

		PngPicture picture = PngReader.Read(PngWriter.Encode(pixels, 17, 9));

		Assert.Equal(17, picture.Width);
		Assert.Equal(9, picture.Height);
		Assert.Equal(pixels, picture.Pixels);
	}

	[Fact]
	public void Encode_KeepsAlpha()
	{
		byte[] pixels = [0, 0, 0, 0, 255, 255, 255, 128, 10, 20, 30, 255, 40, 50, 60, 1];

		PngPicture picture = PngReader.Read(PngWriter.Encode(pixels, 2, 2));

		Assert.Equal(pixels, picture.Pixels);
	}

	[Fact]
	public void Encode_OneByOne_RoundTrips()
	{
		byte[] pixels = [1, 2, 3, 4];

		PngPicture picture = PngReader.Read(PngWriter.Encode(pixels, 1, 1));

		Assert.Equal(1, picture.Width);
		Assert.Equal(1, picture.Height);
		Assert.Equal(pixels, picture.Pixels);
	}

	[Fact]
	public void Encode_ASolidColour_CompressesWell()
	{
		byte[] pixels = new byte[256 * 256 * 4];
		Array.Fill(pixels, (byte)0x30);

		byte[] png = PngWriter.Encode(pixels, 256, 256);

		Assert.True(png.Length < pixels.Length / 20, $"A flat colour should compress far below {pixels.Length} bytes, not {png.Length}.");
		Assert.Equal(pixels, PngReader.Read(png).Pixels);
	}

	[Fact]
	public void Encode_RandomNoise_StillRoundTrips()
	{
		byte[] pixels = new byte[64 * 40 * 4];
		new Random(20260917).NextBytes(pixels);

		Assert.Equal(pixels, PngReader.Read(PngWriter.Encode(pixels, 64, 40)).Pixels);
	}

	[Theory]
	[InlineData(0, 4)]
	[InlineData(4, 0)]
	[InlineData(-1, 4)]
	public void Encode_RejectsASizeThatIsNotPositive(int width, int height) =>
		Assert.Throws<ArgumentOutOfRangeException>(() => PngWriter.Encode(new byte[64], width, height));

	[Fact]
	public void Encode_RejectsPixelsThatDoNotMatchTheSize()
	{
		ArgumentException error = Assert.Throws<ArgumentException>(() => PngWriter.Encode(new byte[10], 4, 4));

		Assert.Equal("rgba", error.ParamName);
	}
}
