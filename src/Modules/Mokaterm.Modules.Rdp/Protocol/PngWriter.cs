using System.Buffers.Binary;
using System.IO.Compression;

namespace Mokaterm.Modules.Rdp.Protocol;

/// <summary>
/// Writes 8 bit RGBA PNGs. Written here rather than taken from a package because the module needs exactly one
/// colour type and one filter, and a frame encoder on the hot path should not pull an imaging library into
/// every host.
/// </summary>
internal static class PngWriter
{
	/// <summary>Bytes per pixel in the only layout this writer takes.</summary>
	public const int BytesPerPixel = 4;

	// Every chunk is its length, its type, its data and a CRC.
	private const int ChunkOverhead = 12;
	private const int HeaderDataLength = 13;

	private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

	private static readonly uint[] CrcTable = BuildCrcTable();

	/// <param name="rgba">Rows of <paramref name="width"/> RGBA pixels, top row first, with no padding.</param>
	/// <exception cref="ArgumentOutOfRangeException">The size is not positive.</exception>
	/// <exception cref="ArgumentException"><paramref name="rgba"/> does not hold exactly one picture of that size.</exception>
	public static byte[] Encode(ReadOnlySpan<byte> rgba, int width, int height)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
		ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
		int expected = checked(width * height * BytesPerPixel);
		if (rgba.Length != expected)
		{
			throw new ArgumentException($"A {width} by {height} picture needs {expected} bytes, not {rgba.Length}.", nameof(rgba));
		}

		using MemoryStream compressed = new((expected / 2) + 64);
		return Encode(rgba, width * BytesPerPixel, new RdpRegion(0, 0, width, height), opaque: false, prefixLength: 0, compressed, []);
	}

	/// <summary>
	/// Writes <paramref name="region"/> of a picture held as rows of <paramref name="stride"/> bytes as a PNG, after
	/// <paramref name="prefixLength"/> bytes left for the caller, into one array of exactly the size needed. The rows are
	/// deflated where they lie, so nothing the size of the region is copied on the way.
	/// </summary>
	/// <param name="opaque">Writes every alpha byte as 255. Each row then passes through <paramref name="row"/>, which must hold one.</param>
	/// <param name="compressed">Scratch for the deflated rows, emptied first. Reusing one keeps its buffer from frame to frame.</param>
	/// <exception cref="ArgumentException">The region is empty or reaches outside <paramref name="source"/>.</exception>
	public static byte[] Encode(
		ReadOnlySpan<byte> source,
		int stride,
		RdpRegion region,
		bool opaque,
		int prefixLength,
		MemoryStream compressed,
		Span<byte> row)
	{
		ArgumentNullException.ThrowIfNull(compressed);
		ArgumentOutOfRangeException.ThrowIfNegative(prefixLength);
		int rowBytes = checked(region.Width * BytesPerPixel);
		if (region.IsEmpty || region.X < 0 || region.Y < 0 || stride < region.Right * BytesPerPixel
			|| (long)(region.Bottom - 1) * stride + (region.Right * BytesPerPixel) > source.Length)
		{
			throw new ArgumentException($"The region {region} is not inside the picture.", nameof(region));
		}

		if (opaque && row.Length < rowBytes)
		{
			throw new ArgumentException($"Making a {region.Width} pixel row opaque needs {rowBytes} bytes of room.", nameof(row));
		}

		compressed.SetLength(0);
		using (ZLibStream deflate = new(compressed, CompressionLevel.Fastest, leaveOpen: true))
		{
			// Filter 0 on every row: the picture is already small, and a per-row filter search costs more time
			// than the bytes it saves at this frame rate.
			ReadOnlySpan<byte> none = [0];
			for (int y = 0; y < region.Height; y++)
			{
				ReadOnlySpan<byte> line = source.Slice(((region.Y + y) * stride) + (region.X * BytesPerPixel), rowBytes);
				deflate.Write(none);
				if (opaque)
				{
					Span<byte> target = row[..rowBytes];
					line.CopyTo(target);
					MakeOpaque(target);
					deflate.Write(target);
				}
				else
				{
					deflate.Write(line);
				}
			}
		}

		ReadOnlySpan<byte> pixels = compressed.GetBuffer().AsSpan(0, (int)compressed.Length);
		int fileLength = Signature.Length + (ChunkOverhead + HeaderDataLength) + (ChunkOverhead + pixels.Length) + ChunkOverhead;
		byte[] file = GC.AllocateUninitializedArray<byte>(prefixLength + fileLength);
		Span<byte> at = file.AsSpan(prefixLength);
		Signature.CopyTo(at);
		at = at[Signature.Length..];

		Span<byte> header = stackalloc byte[HeaderDataLength];
		BinaryPrimitives.WriteUInt32BigEndian(header[..4], (uint)region.Width);
		BinaryPrimitives.WriteUInt32BigEndian(header.Slice(4, 4), (uint)region.Height);
		header[8] = 8;
		header[9] = 6;
		header[10] = 0;
		header[11] = 0;
		header[12] = 0;
		at = WriteChunk(at, "IHDR", header);
		at = WriteChunk(at, "IDAT", pixels);
		WriteChunk(at, "IEND", []);
		return file;
	}

	/// <summary>Sets the fourth byte of every pixel to 255.</summary>
	public static void MakeOpaque(Span<byte> rgba)
	{
		for (int i = BytesPerPixel - 1; i < rgba.Length; i += BytesPerPixel)
		{
			rgba[i] = 0xFF;
		}
	}

	/// <summary>Writes one chunk at the start of <paramref name="target"/> and returns what is left after it.</summary>
	private static Span<byte> WriteChunk(Span<byte> target, string type, scoped ReadOnlySpan<byte> data)
	{
		BinaryPrimitives.WriteUInt32BigEndian(target, (uint)data.Length);
		Span<byte> name = target.Slice(4, 4);
		for (int i = 0; i < 4; i++)
		{
			name[i] = (byte)type[i];
		}

		data.CopyTo(target[8..]);
		uint crc = Crc(Crc(0xFFFFFFFF, name), data) ^ 0xFFFFFFFF;
		BinaryPrimitives.WriteUInt32BigEndian(target.Slice(8 + data.Length, 4), crc);
		return target[(ChunkOverhead + data.Length)..];
	}

	private static uint Crc(uint running, ReadOnlySpan<byte> data)
	{
		uint crc = running;
		foreach (byte value in data)
		{
			crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
		}

		return crc;
	}

	private static uint[] BuildCrcTable()
	{
		uint[] table = new uint[256];
		for (uint i = 0; i < 256; i++)
		{
			uint value = i;
			for (int bit = 0; bit < 8; bit++)
			{
				value = (value & 1) != 0 ? 0xEDB88320 ^ (value >> 1) : value >> 1;
			}

			table[i] = value;
		}

		return table;
	}
}
