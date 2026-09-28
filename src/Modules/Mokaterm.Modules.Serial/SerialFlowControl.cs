namespace Mokaterm.Modules.Serial;

/// <summary>Who is allowed to pause the other side. Mirrors <c>System.IO.Ports.Handshake</c>.</summary>
public enum SerialFlowControl
{
	/// <summary>Nothing holds the line back. What almost every console cable uses.</summary>
	None,

	/// <summary>XON and XOFF in the data stream, so 0x11 and 0x13 stop being data.</summary>
	XOnXOff,

	/// <summary>The RTS and CTS pins. The line settings then own RTS, so it cannot be toggled by hand.</summary>
	RtsCts,

	/// <summary>Both at once, for hardware that asks for it.</summary>
	RtsCtsXOnXOff,
}
