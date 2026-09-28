using System.Globalization;

namespace Mokaterm.Modules.Telnet.Protocol;

/// <summary>Option codes. Only the ones this module answers with something other than a refusal are named.</summary>
internal static class TelnetOption
{
	/// <summary>RFC 856: 8 bit clean transmission, which every character set past US-ASCII needs.</summary>
	public const byte Binary = 0;

	/// <summary>RFC 857. The server enabling it turns local echo off.</summary>
	public const byte Echo = 1;

	/// <summary>RFC 858. Both sides enabling it is what puts the session in character at a time mode.</summary>
	public const byte SuppressGoAhead = 3;

	public const byte Status = 5;

	public const byte TimingMark = 6;

	/// <summary>RFC 1091: the server asks with SB SEND and gets SB IS &lt;name&gt;.</summary>
	public const byte TerminalType = 24;

	public const byte EndOfRecord = 25;

	/// <summary>RFC 1073: the window size, sent again on every resize.</summary>
	public const byte NegotiateAboutWindowSize = 31;

	public const byte TerminalSpeed = 32;

	public const byte RemoteFlowControl = 33;

	public const byte LineMode = 34;

	public const byte XDisplayLocation = 35;

	public const byte OldEnvironment = 36;

	public const byte Authentication = 37;

	public const byte Encryption = 38;

	public const byte NewEnvironment = 39;

	public const byte Charset = 42;

	/// <summary>Subnegotiation command in RFC 1091 and RFC 1572: "here is my value".</summary>
	public const byte Is = 0;

	/// <summary>Subnegotiation command in RFC 1091 and RFC 1572: "send me your value".</summary>
	public const byte Send = 1;

	public static string Name(byte option) => option switch
	{
		Binary => "BINARY",
		Echo => "ECHO",
		SuppressGoAhead => "SUPPRESS-GO-AHEAD",
		Status => "STATUS",
		TimingMark => "TIMING-MARK",
		TerminalType => "TERMINAL-TYPE",
		EndOfRecord => "END-OF-RECORD",
		NegotiateAboutWindowSize => "NAWS",
		TerminalSpeed => "TERMINAL-SPEED",
		RemoteFlowControl => "TOGGLE-FLOW-CONTROL",
		LineMode => "LINEMODE",
		XDisplayLocation => "X-DISPLAY-LOCATION",
		OldEnvironment => "OLD-ENVIRON",
		Authentication => "AUTHENTICATION",
		Encryption => "ENCRYPT",
		NewEnvironment => "NEW-ENVIRON",
		Charset => "CHARSET",
		_ => option.ToString(CultureInfo.InvariantCulture),
	};
}
