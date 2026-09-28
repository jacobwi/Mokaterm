using System.Buffers.Binary;

namespace Mokaterm.Modules.Rdp.Protocol;

/// <summary>
/// Packs one changed region for the page: a header, then the pixels. Small regions travel as raw RGBA because
/// both sides handle them without work; large ones are compressed, since a full screen of raw is megabytes and
/// the web host pushes every frame through one SignalR message.
/// </summary>
internal sealed class RdpFrameEncoder
{
	/// <summary>Six little-endian 16 bit fields. The last one is padding that puts the pixels on a four byte boundary.</summary>
	public const int HeaderLength = 12;

	public const int RawFormat = 0;

	public const int PngFormat = 1;

	private byte[] _buffer = [];

	/// <param name="rawLimitBytes">Regions whose pixels fit in this many bytes are sent raw.</param>
	public RdpFrameEncoder(int rawLimitBytes) => RawLimitBytes = Math.Max(0, rawLimitBytes);

	public int RawLimitBytes { get; }

	/// <summary>The format <see cref="Encode"/> would pick for a region of this size.</summary>
	public int FormatFor(RdpRegion region) =>
		region.Area * PngWriter.BytesPerPixel <= RawLimitBytes ? RawFormat : PngFormat;

	/// <summary>
	/// Cuts <paramref name="region"/> out of a desktop held as rows of RGBA pixels and returns the message for it.
	/// The result points into a buffer this encoder reuses, so send it before the next call.
	/// </summary>
	public ReadOnlyMemory<byte> Encode(ReadOnlySpan<byte> desktop, int desktopWidth, RdpRegion region)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(desktopWidth, 1);
		if (region.IsEmpty)
		{
			throw new ArgumentException("An empty region has nothing to send.", nameof(region));
		}

		int pixelBytes = (int)region.Area * PngWriter.BytesPerPixel;
		bool raw = pixelBytes <= RawLimitBytes;
		int needed = raw ? HeaderLength + pixelBytes : HeaderLength;
		if (_buffer.Length < needed)
		{
			_buffer = new byte[needed];
		}

		WriteHeader(_buffer, raw ? RawFormat : PngFormat, region);
		if (raw)
		{
			CopyRegion(desktop, desktopWidth, region, _buffer.AsSpan(HeaderLength, pixelBytes));
			return _buffer.AsMemory(0, needed);
		}

		byte[] pixels = new byte[pixelBytes];
		CopyRegion(desktop, desktopWidth, region, pixels);
		byte[] png = PngWriter.Encode(pixels, region.Width, region.Height);
		if (_buffer.Length < HeaderLength + png.Length)
		{
			byte[] grown = new byte[HeaderLength + png.Length];
			_buffer.AsSpan(0, HeaderLength).CopyTo(grown);
			_buffer = grown;
		}

		png.CopyTo(_buffer.AsSpan(HeaderLength));
		return _buffer.AsMemory(0, HeaderLength + png.Length);
	}

	private static void WriteHeader(Span<byte> target, int format, RdpRegion region)
	{
		BinaryPrimitives.WriteUInt16LittleEndian(target, (ushort)format);
		BinaryPrimitives.WriteUInt16LittleEndian(target[2..], (ushort)region.X);
		BinaryPrimitives.WriteUInt16LittleEndian(target[4..], (ushort)region.Y);
		BinaryPrimitives.WriteUInt16LittleEndian(target[6..], (ushort)region.Width);
		BinaryPrimitives.WriteUInt16LittleEndian(target[8..], (ushort)region.Height);
		BinaryPrimitives.WriteUInt16LittleEndian(target[10..], 0);
	}

	private static void CopyRegion(ReadOnlySpan<byte> desktop, int desktopWidth, RdpRegion region, Span<byte> target)
	{
		int stride = desktopWidth * PngWriter.BytesPerPixel;
		int rowBytes = region.Width * PngWriter.BytesPerPixel;
		for (int row = 0; row < region.Height; row++)
		{
			int source = ((region.Y + row) * stride) + (region.X * PngWriter.BytesPerPixel);
			desktop.Slice(source, rowBytes).CopyTo(target.Slice(row * rowBytes, rowBytes));
		}

		// The desktop has nothing behind it, and a codec that leaves the fourth byte alone would otherwise paint
		// holes in the canvas.
		for (int i = PngWriter.BytesPerPixel - 1; i < target.Length; i += PngWriter.BytesPerPixel)
		{
			target[i] = 0xFF;
		}
	}
}
