namespace Mokaterm.Sessions.Zmodem;

/// <summary>How a header's check bytes were encoded, which also says how following data subpackets are checked.</summary>
internal enum ZmodemHeaderEncoding
{
	/// <summary><c>ZPAD ZPAD ZDLE ZHEX</c> plus 16 hex digits and CR LF (an XON unless the frame asks for none).</summary>
	Hex,

	/// <summary><c>ZPAD ZDLE ZBIN</c> plus ZDLE-escaped bytes and a 16-bit CRC.</summary>
	Binary16,

	/// <summary><c>ZPAD ZPAD ZDLE ZBIN32</c> plus ZDLE-escaped bytes and a 32-bit CRC.</summary>
	Binary32,
}

/// <summary>
/// One parsed or about-to-be-sent ZMODEM header: a frame type and its four data bytes, in wire order. The protocol
/// reads them two ways: numeric frames (ZRPOS, ZACK, ZDATA, ZEOF, ...) carry a position with <see cref="B0"/> the least
/// significant byte, while flag frames (ZRINIT, ZFILE, ...) name their flags ZF0 to ZF3, ZF0 being <see cref="B3"/>, the
/// last byte on the wire.
/// </summary>
/// <remarks>Byte order verified against lrzsz's <c>stohdr</c>/<c>rclhdr</c> and the specification's "beware of the catch" note.</remarks>
internal readonly record struct ZmodemHeader(byte Type, byte B0 = 0, byte B1 = 0, byte B2 = 0, byte B3 = 0, ZmodemHeaderEncoding Encoding = ZmodemHeaderEncoding.Hex)
{
	/// <summary>The four data bytes as a 32-bit position, least significant byte first.</summary>
	public long Position => (uint)(B0 | (B1 << 8) | (B2 << 16) | (B3 << 24));

	/// <summary>The ZRINIT capability flags of this header, which live in ZF0 (<see cref="B3"/>).</summary>
	public byte Capabilities => B3;

	/// <summary>The receive buffer size a ZRINIT offers, a 16-bit value in ZP0/ZP1 (<see cref="B0"/> and <see cref="B1"/>); 0 means no limit.</summary>
	public int BufferSize => B0 | (B1 << 8);

	/// <summary>A header whose four bytes carry a position, such as ZRPOS or ZEOF.</summary>
	public static ZmodemHeader OfPosition(byte type, long position, ZmodemHeaderEncoding encoding = ZmodemHeaderEncoding.Hex)
	{
		uint value = (uint)position;
		return new ZmodemHeader(type, (byte)value, (byte)(value >> 8), (byte)(value >> 16), (byte)(value >> 24), encoding);
	}

	/// <summary>Our ZRINIT: no buffer limit, no flow-control needs, and the capabilities the peer may use.</summary>
	public static ZmodemHeader OurZrinit() => new(ZmodemCode.ZRINIT, 0, 0, 0, ZmodemCode.OurCapabilities);
}
