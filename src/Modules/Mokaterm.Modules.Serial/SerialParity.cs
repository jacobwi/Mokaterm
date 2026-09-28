namespace Mokaterm.Modules.Serial;

/// <summary>
/// The parity bit on the line. Mirrors <c>System.IO.Ports.Parity</c> so nothing outside
/// <see cref="Protocol.SerialPortLink"/> has to touch a type that is unsupported on mobile targets.
/// </summary>
public enum SerialParity
{
	None,

	Odd,

	Even,

	/// <summary>The parity bit is always 1. Used by a few nine-bit addressing schemes.</summary>
	Mark,

	/// <summary>The parity bit is always 0.</summary>
	Space,
}
