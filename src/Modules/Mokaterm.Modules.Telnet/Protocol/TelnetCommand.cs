using System.Globalization;

namespace Mokaterm.Modules.Telnet.Protocol;

/// <summary>The command codes that follow IAC (RFC 854).</summary>
internal static class TelnetCommand
{
	public const byte Se = 240;

	public const byte Nop = 241;

	public const byte DataMark = 242;

	public const byte Break = 243;

	public const byte InterruptProcess = 244;

	public const byte AbortOutput = 245;

	public const byte AreYouThere = 246;

	public const byte EraseCharacter = 247;

	public const byte EraseLine = 248;

	public const byte GoAhead = 249;

	public const byte Sb = 250;

	public const byte Will = 251;

	public const byte Wont = 252;

	public const byte Do = 253;

	public const byte Dont = 254;

	public const byte Iac = 255;

	public static bool IsNegotiation(byte command) => command is Will or Wont or Do or Dont;

	public static string Name(byte command) => command switch
	{
		Se => "SE",
		Nop => "NOP",
		DataMark => "DM",
		Break => "BRK",
		InterruptProcess => "IP",
		AbortOutput => "AO",
		AreYouThere => "AYT",
		EraseCharacter => "EC",
		EraseLine => "EL",
		GoAhead => "GA",
		Sb => "SB",
		Will => "WILL",
		Wont => "WONT",
		Do => "DO",
		Dont => "DONT",
		Iac => "IAC",
		_ => command.ToString(CultureInfo.InvariantCulture),
	};
}
