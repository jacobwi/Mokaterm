namespace Mokaterm.Modules.Serial;

/// <summary>
/// Stop bits on the line. Mirrors <c>System.IO.Ports.StopBits</c> without its <c>None</c> value, which no port
/// can be opened with.
/// </summary>
public enum SerialStopBits
{
	One,

	/// <summary>Only legal with five data bits; every other width is refused before the port is opened.</summary>
	OnePointFive,

	Two,
}
