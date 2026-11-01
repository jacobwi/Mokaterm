namespace Mokaterm.Sessions.Zmodem;

/// <summary>Bytes and values of the ZMODEM protocol, as Chuck Forsberg's specification and lrzsz define them.</summary>
internal static class ZmodemCode
{
	public const byte ZPAD = (byte)'*';
	public const byte ZDLE = 0x18;
	public const byte ZDLEE = ZDLE ^ 0x40;

	/// <summary>A 16-bit CRC binary header follows (<c>ZPAD ZDLE ZBIN</c>).</summary>
	public const byte ZBIN = (byte)'A';

	/// <summary>A hex header follows (<c>ZPAD ZPAD ZDLE ZHEX</c>).</summary>
	public const byte ZHEX = (byte)'B';

	/// <summary>A 32-bit CRC binary header follows (<c>ZPAD ZPAD ZDLE ZBIN32</c>).</summary>
	public const byte ZBIN32 = (byte)'C';

	public const byte XON = 0x11;
	public const byte XOFF = 0x13;
	public const byte Cancel = ZDLE;

	// Frame types.
	public const byte ZRQINIT = 0;
	public const byte ZRINIT = 1;
	public const byte ZSINIT = 2;
	public const byte ZACK = 3;
	public const byte ZFILE = 4;
	public const byte ZSKIP = 5;
	public const byte ZNAK = 6;
	public const byte ZABORT = 7;
	public const byte ZFIN = 8;
	public const byte ZRPOS = 9;
	public const byte ZDATA = 10;
	public const byte ZEOF = 11;
	public const byte ZFERR = 12;
	public const byte ZCRC = 13;
	public const byte ZCHALLENGE = 14;
	public const byte ZCOMPL = 15;
	public const byte ZCAN = 16;
	public const byte ZFREECNT = 17;
	public const byte ZCOMMAND = 18;
	public const byte ZSTDERR = 19;

	// Data subpacket endings: the byte after the final ZDLE of a subpacket's data.
	public const byte ZCRCE = (byte)'h';
	public const byte ZCRCG = (byte)'i';
	public const byte ZCRCQ = (byte)'j';
	public const byte ZCRCW = (byte)'k';

	/// <summary>ZDLE-escaped 0x7f.</summary>
	public const byte ZRUB0 = (byte)'l';

	/// <summary>ZDLE-escaped 0xff.</summary>
	public const byte ZRUB1 = (byte)'m';

	// Receiver capability flags of ZRINIT, in the flags byte ZF0 (the last of the four header data bytes).
	public const byte CANFDX = 0x01;
	public const byte CANOVIO = 0x02;
	public const byte CANBRK = 0x04;
	public const byte CANCRY = 0x08;
	public const byte CANLZW = 0x10;
	public const byte CANFC32 = 0x20;
	public const byte ESCCTL = 0x40;
	public const byte ESC8 = 0x80;

	// Conversion options of ZFILE, in the header's ZF0 byte.
	public const byte ZCBIN = 1;
	public const byte ZCNL = 2;
	public const byte ZCRESUM = 3;

	/// <summary>The ZFILE management option in ZF1 that says an existing file of the same name is replaced.</summary>
	public const byte ZMClob = 4;

	/// <summary>What Mokaterm answers with: full duplex, overlapping I/O and 32-bit CRCs; no buffer limit, no escaping demands.</summary>
	public const byte OurCapabilities = CANFDX | CANOVIO | CANFC32;

	/// <summary>Hex digits of a hex header, lower case, as the protocol spells them.</summary>
	public const string HexDigits = "0123456789abcdef";

	/// <summary>The names a trace or an error message can quote for a frame type.</summary>
	public static string Describe(byte frameType) => frameType switch
	{
		ZRQINIT => "ZRQINIT",
		ZRINIT => "ZRINIT",
		ZSINIT => "ZSINIT",
		ZACK => "ZACK",
		ZFILE => "ZFILE",
		ZSKIP => "ZSKIP",
		ZNAK => "ZNAK",
		ZABORT => "ZABORT",
		ZFIN => "ZFIN",
		ZRPOS => "ZRPOS",
		ZDATA => "ZDATA",
		ZEOF => "ZEOF",
		ZFERR => "ZFERR",
		ZCRC => "ZCRC",
		ZCHALLENGE => "ZCHALLENGE",
		ZCOMPL => "ZCOMPL",
		ZCAN => "ZCAN",
		ZFREECNT => "ZFREECNT",
		ZCOMMAND => "ZCOMMAND",
		ZSTDERR => "ZSTDERR",
		_ => $"frame {frameType}",
	};
}
