namespace Mokaterm.Modules.Rdp.Protocol;

/// <summary>
/// A PC/AT set 1 make code as RDP sends it: the byte plus the flag for the keys that are prefixed with E0 on the
/// wire, which is what separates the arrow block from the numeric keypad.
/// </summary>
internal readonly record struct RdpScancode(bool Extended, byte Code)
{
	/// <summary>The pair as one number, the way <c>Scancode.FromU16</c> reads it.</summary>
	public ushort Value => (ushort)((Extended ? 0xE000 : 0) | Code);
}
