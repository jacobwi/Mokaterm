using System.Buffers.Binary;

namespace Mokaterm.Modules.Rdp.Protocol;

/// <summary>
/// Packs one changed region for the page: a header, then the pixels. Small regions travel as raw RGBA because
/// both sides handle them without work; large ones are compressed, since a full screen of raw is megabytes and
/// the web host pushes every frame through one SignalR message.
/// </summary>
/// <remarks>
/// The rows are read straight out of the desktop copy, raw ones into the message and PNG ones into the deflater, so a
/// region costs one copy of its own bytes at most and nothing the size of the desktop. The message is a new array of
/// exactly its size, which is what JS interop needs anyway; the deflate scratch is kept from frame to frame.
/// </remarks>
internal sealed class RdpFrameEncoder : IDisposable
{
	/// <summary>Six little-endian 16 bit fields. The last one is padding that puts the pixels on a four byte boundary.</summary>
	public const int HeaderLength = 12;

	public const int RawFormat = 0;

	public const int PngFormat = 1;

	// A full screen of noise deflates to about its raw size, which would otherwise stay held for the whole session.
	private const int MaxKeptScratchBytes = 4 * 1024 * 1024;

	private readonly MemoryStream _compressed = new();
	private byte[] _row = [];

	/// <param name="rawLimitBytes">Regions whose pixels fit in this many bytes are sent raw.</param>
	public RdpFrameEncoder(int rawLimitBytes) => RawLimitBytes = Math.Max(0, rawLimitBytes);

	public int RawLimitBytes { get; }

	/// <summary>The format <see cref="Encode"/> would pick for a region of this size.</summary>
	public int FormatFor(RdpRegion region) =>
		region.Area * PngWriter.BytesPerPixel <= RawLimitBytes ? RawFormat : PngFormat;

	/// <summary>
	/// Cuts <paramref name="region"/> out of a desktop held as rows of RGBA pixels and returns the message for it, with
	/// every pixel opaque. The array is new and the caller's to keep.
	/// </summary>
	/// <exception cref="ArgumentException">The region is empty or reaches outside the desktop.</exception>
	public byte[] Encode(ReadOnlySpan<byte> desktop, int desktopWidth, RdpRegion region)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(desktopWidth, 1);
		if (region.IsEmpty)
		{
			throw new ArgumentException("An empty region has nothing to send.", nameof(region));
		}

		int stride = desktopWidth * PngWriter.BytesPerPixel;
		if (region.X < 0 || region.Y < 0 || region.Right > desktopWidth || (long)region.Bottom * stride > desktop.Length)
		{
			throw new ArgumentException($"The region {region} is not inside the desktop.", nameof(region));
		}

		int rowBytes = region.Width * PngWriter.BytesPerPixel;
		if (FormatFor(region) == RawFormat)
		{
			byte[] message = GC.AllocateUninitializedArray<byte>(HeaderLength + (rowBytes * region.Height));
			WriteHeader(message, RawFormat, region);
			Span<byte> pixels = message.AsSpan(HeaderLength);
			for (int row = 0; row < region.Height; row++)
			{
				int source = ((region.Y + row) * stride) + (region.X * PngWriter.BytesPerPixel);
				desktop.Slice(source, rowBytes).CopyTo(pixels.Slice(row * rowBytes, rowBytes));
			}

			// The desktop has nothing behind it, and a codec that leaves the fourth byte alone would otherwise paint
			// holes in the canvas.
			PngWriter.MakeOpaque(pixels);
			return message;
		}

		if (_row.Length < rowBytes)
		{
			_row = new byte[rowBytes];
		}

		byte[] png = PngWriter.Encode(desktop, stride, region, opaque: true, HeaderLength, _compressed, _row);
		WriteHeader(png, PngFormat, region);
		if (_compressed.Capacity > MaxKeptScratchBytes)
		{
			_compressed.SetLength(0);
			_compressed.Capacity = 0;
		}

		return png;
	}

	public void Dispose() => _compressed.Dispose();

	private static void WriteHeader(Span<byte> target, int format, RdpRegion region)
	{
		BinaryPrimitives.WriteUInt16LittleEndian(target, (ushort)format);
		BinaryPrimitives.WriteUInt16LittleEndian(target[2..], (ushort)region.X);
		BinaryPrimitives.WriteUInt16LittleEndian(target[4..], (ushort)region.Y);
		BinaryPrimitives.WriteUInt16LittleEndian(target[6..], (ushort)region.Width);
		BinaryPrimitives.WriteUInt16LittleEndian(target[8..], (ushort)region.Height);
		BinaryPrimitives.WriteUInt16LittleEndian(target[10..], 0);
	}
}
