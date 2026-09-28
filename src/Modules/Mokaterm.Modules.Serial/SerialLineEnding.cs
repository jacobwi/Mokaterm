namespace Mokaterm.Modules.Serial;

/// <summary>What the Enter key sends down the line.</summary>
public enum SerialLineEnding
{
	/// <summary>CR alone. What bootloaders, switch consoles and most embedded shells expect.</summary>
	Cr,

	/// <summary>CR LF, for devices that want a full new line.</summary>
	CrLf,

	/// <summary>LF alone, which a Linux console reached over a serial port treats as Enter.</summary>
	Lf,
}
