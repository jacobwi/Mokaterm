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
		RdpFrameEncoder encoder = new(64 * 1024);
		byte[] desktop = RdpHarness.Gradient(DesktopWidth, DesktopHeight);
		RdpRegion region = new(3, 5, 8, 4);

		ReadOnlyMemory<byte> frame = encoder.Encode(desktop, DesktopWidth, region);

		Assert.Equal(RdpFrameEncoder.RawFormat, ReadField(frame, 0));
		Assert.Equal(3, ReadField(frame, 1));
		Assert.Equal(5, ReadField(frame, 2));
		Assert.Equal(8, ReadField(frame, 3));
		Assert.Equal(4, ReadField(frame, 4));
		Assert.Equal(RdpFrameEncoder.HeaderLength + (8 * 4 * 4), frame.Length);
		Assert.Equal(Cut(desktop, DesktopWidth, region), frame.Span[RdpFrameEncoder.HeaderLength..].ToArray());
	}

	[Fact]
	public void Encode_LargeRegion_SendsAPng()
	{
		// Anything wider than four pixels is over this limit, so the whole region goes as a picture.
		RdpFrameEncoder encoder = new(64);
		byte[] desktop = RdpHarness.Gradient(DesktopWidth, DesktopHeight);
		RdpRegion region = new(0, 0, 32, 16);

		ReadOnlyMemory<byte> frame = encoder.Encode(desktop, DesktopWidth, region);

		Assert.Equal(RdpFrameEncoder.PngFormat, ReadField(frame, 0));
		Assert.Equal(32, ReadField(frame, 3));
		Assert.Equal(16, ReadField(frame, 4));
		PngPicture picture = PngReader.Read(frame.Span[RdpFrameEncoder.HeaderLength..].ToArray());
		Assert.Equal(32, picture.Width);
		Assert.Equal(16, picture.Height);
		Assert.Equal(Cut(desktop, DesktopWidth, region), picture.Pixels);
	}

	[Fact]
	public void Encode_SwitchesFormatExactlyAtTheLimit()
	{
		// Sixteen pixels of four bytes each.
		RdpFrameEncoder encoder = new(64);
		byte[] desktop = RdpHarness.Gradient(DesktopWidth, DesktopHeight);

		Assert.Equal(RdpFrameEncoder.RawFormat, encoder.FormatFor(new RdpRegion(0, 0, 4, 4)));
		Assert.Equal(RdpFrameEncoder.PngFormat, encoder.FormatFor(new RdpRegion(0, 0, 4, 5)));
		Assert.Equal(RdpFrameEncoder.RawFormat, ReadField(encoder.Encode(desktop, DesktopWidth, new RdpRegion(0, 0, 4, 4)), 0));
		Assert.Equal(RdpFrameEncoder.PngFormat, ReadField(encoder.Encode(desktop, DesktopWidth, new RdpRegion(0, 0, 4, 5)), 0));
	}

	[Fact]
	public void Encode_ForcesEveryPixelOpaque()
	{
		RdpFrameEncoder encoder = new(64 * 1024);
		byte[] desktop = new byte[DesktopWidth * DesktopHeight * 4];

		ReadOnlyMemory<byte> frame = encoder.Encode(desktop, DesktopWidth, new RdpRegion(0, 0, 2, 2));

		byte[] pixels = frame.Span[RdpFrameEncoder.HeaderLength..].ToArray();
		Assert.Equal<byte[]>([0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255], pixels);
	}

	[Fact]
	public void Encode_TakesTheRowsFromTheRightPlace()
	{
		RdpFrameEncoder encoder = new(64 * 1024);
		byte[] desktop = RdpHarness.Gradient(DesktopWidth, DesktopHeight);
		RdpRegion region = new(DesktopWidth - 2, DesktopHeight - 3, 2, 3);

		ReadOnlyMemory<byte> frame = encoder.Encode(desktop, DesktopWidth, region);

		Assert.Equal(Cut(desktop, DesktopWidth, region), frame.Span[RdpFrameEncoder.HeaderLength..].ToArray());
	}

	[Fact]
	public void Encode_ReusesItsBufferBetweenCalls()
	{
		RdpFrameEncoder encoder = new(64 * 1024);
		byte[] desktop = RdpHarness.Gradient(DesktopWidth, DesktopHeight);

		ReadOnlyMemory<byte> first = encoder.Encode(desktop, DesktopWidth, new RdpRegion(0, 0, 8, 8));
		byte[] copy = first.ToArray();
		ReadOnlyMemory<byte> second = encoder.Encode(desktop, DesktopWidth, new RdpRegion(8, 8, 8, 8));

		Assert.NotEqual(copy, second.ToArray());
		Assert.Equal(first.Length, second.Length);
	}

	[Fact]
	public void Encode_RejectsAnEmptyRegion()
	{
		RdpFrameEncoder encoder = new(1024);

		Assert.Throws<ArgumentException>(() => encoder.Encode(new byte[64], 4, new RdpRegion(0, 0, 0, 4)));
	}

	[Fact]
	public void RawLimitBytes_IsNeverNegative() => Assert.Equal(0, new RdpFrameEncoder(-5).RawLimitBytes);

	private static int ReadField(ReadOnlyMemory<byte> frame, int index) =>
		BinaryPrimitives.ReadUInt16LittleEndian(frame.Span.Slice(index * 2, 2));

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
