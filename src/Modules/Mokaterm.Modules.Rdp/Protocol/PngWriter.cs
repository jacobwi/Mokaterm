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

		// A PNG of this picture is never larger than the raw bytes plus the headers by more than a few percent.
		using MemoryStream file = new(expected / 2 + 256);
		file.Write(Signature);
		WriteHeader(file, width, height);
		WritePixels(file, rgba, width, height);
		WriteChunk(file, "IEND", []);
		return file.ToArray();
	}

	private static void WriteHeader(Stream file, int width, int height)
	{
		Span<byte> header = stackalloc byte[13];
		BinaryPrimitives.WriteUInt32BigEndian(header[..4], (uint)width);
		BinaryPrimitives.WriteUInt32BigEndian(header.Slice(4, 4), (uint)height);
		header[8] = 8;
		header[9] = 6;
		header[10] = 0;
		header[11] = 0;
		header[12] = 0;
		WriteChunk(file, "IHDR", header);
	}

	private static void WritePixels(Stream file, ReadOnlySpan<byte> rgba, int width, int height)
	{
		int stride = width * BytesPerPixel;
		using MemoryStream compressed = new(rgba.Length / 2 + 64);
		using (ZLibStream deflate = new(compressed, CompressionLevel.Fastest, leaveOpen: true))
		{
			// Filter 0 on every row: the picture is already small, and a per-row filter search costs more time
			// than the bytes it saves at this frame rate.
			Span<byte> none = [0];
			for (int row = 0; row < height; row++)
			{
				deflate.Write(none);
				deflate.Write(rgba.Slice(row * stride, stride));
			}
		}

		WriteChunk(file, "IDAT", compressed.GetBuffer().AsSpan(0, (int)compressed.Length));
	}

	private static void WriteChunk(Stream file, string type, ReadOnlySpan<byte> data)
	{
		Span<byte> length = stackalloc byte[4];
		BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
		file.Write(length);

		Span<byte> name = stackalloc byte[4];
		for (int i = 0; i < 4; i++)
		{
			name[i] = (byte)type[i];
		}

		file.Write(name);
		file.Write(data);

		uint crc = Crc(Crc(0xFFFFFFFF, name), data) ^ 0xFFFFFFFF;
		Span<byte> checksum = stackalloc byte[4];
		BinaryPrimitives.WriteUInt32BigEndian(checksum, crc);
		file.Write(checksum);
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
