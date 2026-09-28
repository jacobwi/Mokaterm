namespace Mokaterm.Modules.Telnet.Protocol;

/// <summary>
/// One thing the parser pulled out of the stream: a bare command, a negotiation, or a subnegotiation with its
/// payload already unescaped.
/// </summary>
internal readonly record struct TelnetMessage(byte Command, byte Option, byte[] Payload)
{
	public bool IsNegotiation => TelnetCommand.IsNegotiation(Command);

	public bool IsSubnegotiation => Command == TelnetCommand.Sb;

	/// <summary>A command with no option, such as NOP or AYT.</summary>
	public static TelnetMessage Bare(byte command) => new(command, 0, []);

	public static TelnetMessage Negotiation(byte command, byte option) => new(command, option, []);

	public static TelnetMessage Subnegotiation(byte option, byte[] payload) => new(TelnetCommand.Sb, option, payload);

	public override string ToString() => Command switch
	{
		TelnetCommand.Sb => $"SB {TelnetOption.Name(Option)} ({Payload.Length} bytes)",
		_ when IsNegotiation => $"{TelnetCommand.Name(Command)} {TelnetOption.Name(Option)}",
		_ => TelnetCommand.Name(Command),
	};
}
