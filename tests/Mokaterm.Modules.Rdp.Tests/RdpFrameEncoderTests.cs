using System.Buffers.Binary;
using Mokaterm.Modules.Rdp.Protocol;

namespace Mokaterm.Modules.Rdp.Tests;

public sealed class RdpFrameEncoderTests
{
	private const int DesktopWidth = 64;
	private const int DesktopHeight = 48;

	[Fact]
	public void Encode_SmallRegion_SendsRawPixels()
	{
		using RdpFrameEncoder encoder = new(64 * 1024);
		byte[] desktop = RdpHarness.Gradient(DesktopWidth, DesktopHeight);
		RdpRegion region = new(3, 5, 8, 4);

		byte[] frame = encoder.Encode(desktop, DesktopWidth, region);

		Assert.Equal(RdpFrameEncoder.RawFormat, ReadField(frame, 0));
		Assert.Equal(3, ReadField(frame, 1));
		Assert.Equal(5, ReadField(frame, 2));
		Assert.Equal(8, ReadField(frame, 3));
		Assert.Equal(4, ReadField(frame, 4));
		Assert.Equal(0, ReadField(frame, 5));
		Assert.Equal(RdpFrameEncoder.HeaderLength + (8 * 4 * 4), frame.Length);
		Assert.Equal(Cut(desktop, DesktopWidth, region), frame[RdpFrameEncoder.HeaderLength..]);
	}

	[Fact]
	public void Encode_LargeRegion_SendsAPng()
	{
		// Anything wider than four pixels is over this limit, so the whole region goes as a picture.
		using RdpFrameEncoder encoder = new(64);
		byte[] desktop = RdpHarness.Gradient(DesktopWidth, DesktopHeight);
		RdpRegion region = new(0, 0, 32, 16);

		byte[] frame = encoder.Encode(desktop, DesktopWidth, region);

		Assert.Equal(RdpFrameEncoder.PngFormat, ReadField(frame, 0));
		Assert.Equal(32, ReadField(frame, 3));
		Assert.Equal(16, ReadField(frame, 4));
		PngPicture picture = PngReader.Read(frame[RdpFrameEncoder.HeaderLength..]);
		Assert.Equal(32, picture.Width);
		Assert.Equal(16, picture.Height);
		Assert.Equal(Cut(desktop, DesktopWidth, region), picture.Pixels);
	}

	[Fact]
	public void Encode_APngOfARegionInTheMiddle_TakesItsRowsFromTheDesktopStride()
	{
		using RdpFrameEncoder encoder = new(64);
		byte[] desktop = RdpHarness.Gradient(DesktopWidth, DesktopHeight);
		RdpRegion region = new(11, 7, 29, 23);

		byte[] frame = encoder.Encode(desktop, DesktopWidth, region);

		Assert.Equal(RdpFrameEncoder.PngFormat, ReadField(frame, 0));
		Assert.Equal((11, 7), (ReadField(frame, 1), ReadField(frame, 2)));
		Assert.Equal(Cut(desktop, DesktopWidth, region), PngReader.Read(frame[RdpFrameEncoder.HeaderLength..]).Pixels);
	}

	[Fact]
	public void Encode_SwitchesFormatExactlyAtTheLimit()
	{
		// Sixteen pixels of four bytes each.
		using RdpFrameEncoder encoder = new(64);
		byte[] desktop = RdpHarness.Gradient(DesktopWidth, DesktopHeight);

		Assert.Equal(RdpFrameEncoder.RawFormat, encoder.FormatFor(new RdpRegion(0, 0, 4, 4)));
		Assert.Equal(RdpFrameEncoder.PngFormat, encoder.FormatFor(new RdpRegion(0, 0, 4, 5)));
		Assert.Equal(RdpFrameEncoder.RawFormat, ReadField(encoder.Encode(desktop, DesktopWidth, new RdpRegion(0, 0, 4, 4)), 0));
		Assert.Equal(RdpFrameEncoder.PngFormat, ReadField(encoder.Encode(desktop, DesktopWidth, new RdpRegion(0, 0, 4, 5)), 0));
	}

	[Fact]
	public void Encode_ForcesEveryRawPixelOpaque()
	{
		using RdpFrameEncoder encoder = new(64 * 1024);
		byte[] desktop = new byte[DesktopWidth * DesktopHeight * 4];

		byte[] frame = encoder.Encode(desktop, DesktopWidth, new RdpRegion(0, 0, 2, 2));

		Assert.Equal<byte>([0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255], frame[RdpFrameEncoder.HeaderLength..]);
	}

	[Fact]
	public void Encode_ForcesEveryPngPixelOpaque_AndLeavesTheDesktopAlone()
	{
		using RdpFrameEncoder encoder = new(16);
		byte[] desktop = new byte[DesktopWidth * DesktopHeight * 4];

		byte[] frame = encoder.Encode(desktop, DesktopWidth, new RdpRegion(5, 5, 3, 3));

		byte[] pixels = PngReader.Read(frame[RdpFrameEncoder.HeaderLength..]).Pixels;
		Assert.All(Enumerable.Range(0, 9), pixel => Assert.Equal(255, pixels[(pixel * 4) + 3]));
		Assert.All(desktop, value => Assert.Equal(0, value));
	}

	[Fact]
	public void Encode_TakesTheRowsFromTheRightPlace()
	{
		using RdpFrameEncoder encoder = new(64 * 1024);
		byte[] desktop = RdpHarness.Gradient(DesktopWidth, DesktopHeight);
		RdpRegion region = new(DesktopWidth - 2, DesktopHeight - 3, 2, 3);

		byte[] frame = encoder.Encode(desktop, DesktopWidth, region);

		Assert.Equal(Cut(desktop, DesktopWidth, region), frame[RdpFrameEncoder.HeaderLength..]);
	}

	[Theory]
	[InlineData(64 * 1024)]
	[InlineData(16)]
	public void Encode_GivesEveryCallItsOwnArray(int rawLimit)
	{
		// The page sink hands the array to the page as it is, so a later frame must never write into an earlier one.
		using RdpFrameEncoder encoder = new(rawLimit);
		byte[] desktop = RdpHarness.Gradient(DesktopWidth, DesktopHeight);

		byte[] first = encoder.Encode(desktop, DesktopWidth, new RdpRegion(0, 0, 8, 8));
		byte[] kept = [.. first];
		byte[] second = encoder.Encode(desktop, DesktopWidth, new RdpRegion(8, 8, 8, 8));

		Assert.NotSame(first, second);
		Assert.Equal(kept, first);
	}

	[Fact]
	public void Encode_RejectsAnEmptyRegion()
	{
		using RdpFrameEncoder encoder = new(1024);

		Assert.Throws<ArgumentException>(() => encoder.Encode(new byte[64], 4, new RdpRegion(0, 0, 0, 4)));
	}

	[Theory]
	[InlineData(-1, 0, 4, 4)]
	[InlineData(0, -1, 4, 4)]
	[InlineData(62, 0, 4, 4)]
	[InlineData(0, 46, 4, 4)]
	public void Encode_RejectsARegionOutsideTheDesktop(int x, int y, int width, int height)
	{
		using RdpFrameEncoder raw = new(64 * 1024);
		using RdpFrameEncoder png = new(0);
		byte[] desktop = RdpHarness.Gradient(DesktopWidth, DesktopHeight);
		RdpRegion region = new(x, y, width, height);

		Assert.Throws<ArgumentException>(() => raw.Encode(desktop, DesktopWidth, region));
		Assert.Throws<ArgumentException>(() => png.Encode(desktop, DesktopWidth, region));
	}

	[Fact]
	public void RawLimitBytes_IsNeverNegative()
	{
		using RdpFrameEncoder encoder = new(-5);

		Assert.Equal(0, encoder.RawLimitBytes);
	}

	private static int ReadField(byte[] frame, int index) =>
		BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(index * 2, 2));

	private static byte[] Cut(byte[] desktop, int desktopWidth, RdpRegion region)
	{
		byte[] pixels = new byte[region.Width * region.Height * 4];
		for (int row = 0; row < region.Height; row++)
		{
			Array.Copy(
				desktop,
				(((region.Y + row) * desktopWidth) + region.X) * 4,
				pixels,
				row * region.Width * 4,
				region.Width * 4);
		}

		return pixels;
	}
}
