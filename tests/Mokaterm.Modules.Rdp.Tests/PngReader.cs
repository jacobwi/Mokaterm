using System.Buffers.Binary;
using System.IO.Compression;

namespace Mokaterm.Modules.Rdp.Tests;

/// <summary>
/// Reads back what <c>PngWriter</c> writes: 8 bit RGBA, no interlacing, every filter the format defines. Small
/// enough to be obviously right, which is what makes it worth more than comparing against a recorded byte string
/// that a different zlib version would break.
/// </summary>
internal static class PngReader
{
	private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

	public static PngPicture Read(byte[] file)
	{
		ArgumentNullException.ThrowIfNull(file);
		if (!file.AsSpan(0, Signature.Length).SequenceEqual(Signature))
		{
			throw new InvalidDataException("Not a PNG.");
		}

		int width = 0;
		int height = 0;
		int colorType = -1;
		int bitDepth = -1;
		List<string> chunks = [];
		using MemoryStream compressed = new();
		int offset = Signature.Length;
		while (offset < file.Length)
		{
			int length = BinaryPrimitives.ReadInt32BigEndian(file.AsSpan(offset, 4));
			string type = System.Text.Encoding.ASCII.GetString(file, offset + 4, 4);
			ReadOnlySpan<byte> data = file.AsSpan(offset + 8, length);
			uint stored = BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(offset + 8 + length, 4));
			uint actual = Crc(file.AsSpan(offset + 4, 4 + length));
			if (stored != actual)
			{
				throw new InvalidDataException($"The {type} chunk has a broken checksum.");
			}

			chunks.Add(type);
			switch (type)
			{
				case "IHDR":
					width = BinaryPrimitives.ReadInt32BigEndian(data[..4]);
					height = BinaryPrimitives.ReadInt32BigEndian(data.Slice(4, 4));
					bitDepth = data[8];
					colorType = data[9];
					break;
				case "IDAT":
					compressed.Write(data);
					break;
				default:
					break;
			}

			offset += 12 + length;
		}

		if (bitDepth != 8 || colorType != 6)
		{
			throw new InvalidDataException($"Only 8 bit RGBA is read back, not depth {bitDepth} type {colorType}.");
		}

		compressed.Position = 0;
		using ZLibStream inflate = new(compressed, CompressionMode.Decompress);
		using MemoryStream raw = new();
		inflate.CopyTo(raw);
		return new PngPicture(width, height, Unfilter(raw.ToArray(), width, height), chunks);
	}

	private static byte[] Unfilter(byte[] rows, int width, int height)
	{
		const int bytesPerPixel = 4;
		int stride = width * bytesPerPixel;
		if (rows.Length != height * (stride + 1))
		{
			throw new InvalidDataException("The picture data does not match the header.");
		}

		byte[] pixels = new byte[height * stride];
		for (int y = 0; y < height; y++)
		{
			int filter = rows[y * (stride + 1)];
			int source = (y * (stride + 1)) + 1;
			int target = y * stride;
			for (int x = 0; x < stride; x++)
			{
				int left = x >= bytesPerPixel ? pixels[target + x - bytesPerPixel] : 0;
				int up = y > 0 ? pixels[target + x - stride] : 0;
				int upLeft = y > 0 && x >= bytesPerPixel ? pixels[target + x - stride - bytesPerPixel] : 0;
				int value = rows[source + x];
				pixels[target + x] = filter switch
				{
					0 => (byte)value,
					1 => (byte)(value + left),
					2 => (byte)(value + up),
					3 => (byte)(value + ((left + up) / 2)),
					4 => (byte)(value + Paeth(left, up, upLeft)),
					_ => throw new InvalidDataException($"Unknown row filter {filter}."),
				};
			}
		}

		return pixels;
	}

	private static int Paeth(int left, int up, int upLeft)
	{
		int estimate = left + up - upLeft;
		int toLeft = Math.Abs(estimate - left);
		int toUp = Math.Abs(estimate - up);
		int toUpLeft = Math.Abs(estimate - upLeft);
		return toLeft <= toUp && toLeft <= toUpLeft ? left : toUp <= toUpLeft ? up : upLeft;
	}

	private static uint Crc(ReadOnlySpan<byte> data)
	{
		uint crc = 0xFFFFFFFF;
		foreach (byte value in data)
		{
			crc ^= value;
			for (int bit = 0; bit < 8; bit++)
			{
				crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
			}
		}

		return crc ^ 0xFFFFFFFF;
	}
}

internal sealed record PngPicture(int Width, int Height, byte[] Pixels, IReadOnlyList<string> Chunks);
