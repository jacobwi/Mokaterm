using System.Buffers.Binary;

namespace Mokaterm.Modules.Vnc.Protocol;

/// <summary>
/// The ServerInit message: screen size, pixel format and desktop name. The raw bytes are kept because the page side
/// gets the very same message replayed after its synthetic handshake.
/// </summary>
internal sealed record RfbServerInit(byte[] Raw, int Width, int Height, string DesktopName)
{
	/// <summary>Size, pixel format and the length of the name.</summary>
	public const int HeaderLength = 24;

	/// <summary>Desktop names are a few dozen characters; anything larger is a server trying to make us allocate.</summary>
	public const int MaxNameLength = 64 * 1024;

	public static async ValueTask<RfbServerInit> ReadAsync(RfbWire wire, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(wire);
		byte[] header = await wire.ReadBytesAsync(HeaderLength, cancellationToken);
		uint nameLength = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(20, 4));
		if (nameLength > MaxNameLength)
		{
			throw new VncProtocolException($"The server sent a desktop name of {nameLength} bytes.");
		}

		byte[] raw = new byte[HeaderLength + (int)nameLength];
		header.CopyTo(raw, 0);
		await wire.ReadExactAsync(raw.AsMemory(HeaderLength), cancellationToken);
		return new RfbServerInit(
			raw,
			BinaryPrimitives.ReadUInt16BigEndian(raw.AsSpan(0, 2)),
			BinaryPrimitives.ReadUInt16BigEndian(raw.AsSpan(2, 2)),
			RfbWire.DecodeText(raw.AsSpan(HeaderLength)));
	}
}
