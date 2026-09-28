using System.Buffers.Binary;

namespace Mokaterm.DevHost.Demo.Vnc;

/// <summary>The pixel format a viewer asked for in SetPixelFormat.</summary>
internal readonly record struct DemoPixelFormat(
	int BitsPerPixel,
	bool BigEndian,
	bool TrueColour,
	int RedMax,
	int GreenMax,
	int BlueMax,
	int RedShift,
	int GreenShift,
	int BlueShift)
{
	/// <summary>What noVNC asks for: 32 bits, little endian, eight bits per channel.</summary>
	public static DemoPixelFormat Default { get; } = new(32, false, true, 255, 255, 255, 16, 8, 0);

	public int BytesPerPixel => Math.Max(1, BitsPerPixel / 8);

	/// <summary>Reads the sixteen byte PIXEL_FORMAT. Formats the demo screen cannot serve fall back to the default.</summary>
	public static DemoPixelFormat Parse(ReadOnlySpan<byte> bytes)
	{
		DemoPixelFormat format = new(
			bytes[0],
			bytes[2] != 0,
			bytes[3] != 0,
			BinaryPrimitives.ReadUInt16BigEndian(bytes[4..6]),
			BinaryPrimitives.ReadUInt16BigEndian(bytes[6..8]),
			BinaryPrimitives.ReadUInt16BigEndian(bytes[8..10]),
			bytes[10],
			bytes[11],
			bytes[12]);

		return format is { TrueColour: true, BitsPerPixel: 8 or 16 or 32 } ? format : Default;
	}

	/// <summary>Writes one 0x00RRGGBB pixel in this format.</summary>
	public void Write(uint pixel, Span<byte> destination)
	{
		uint red = Scale((pixel >> 16) & 0xFF, RedMax);
		uint green = Scale((pixel >> 8) & 0xFF, GreenMax);
		uint blue = Scale(pixel & 0xFF, BlueMax);
		uint value = (red << RedShift) | (green << GreenShift) | (blue << BlueShift);
		switch (BytesPerPixel)
		{
			case 1:
				destination[0] = (byte)value;
				break;
			case 2:
				if (BigEndian)
				{
					BinaryPrimitives.WriteUInt16BigEndian(destination, (ushort)value);
				}
				else
				{
					BinaryPrimitives.WriteUInt16LittleEndian(destination, (ushort)value);
				}

				break;
			default:
				if (BigEndian)
				{
					BinaryPrimitives.WriteUInt32BigEndian(destination, value);
				}
				else
				{
					BinaryPrimitives.WriteUInt32LittleEndian(destination, value);
				}

				break;
		}
	}

	private static uint Scale(uint channel, int max) => max >= 255 ? channel : channel * (uint)max / 255;
}
