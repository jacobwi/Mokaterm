namespace Mokaterm.Modules.Rdp.Protocol;

internal enum RdpOperationKind
{
	MouseMove,
	MouseDown,
	MouseUp,
	Wheel,
	KeyDown,
	KeyUp,
}

/// <summary>
/// One thing to hand IronRDP, worked out from a page event. Keeping it as plain numbers lets the translator be
/// tested without a connection, and only <see cref="RdpOperations"/> touches the native handles.
/// </summary>
/// <param name="Kind">What to do.</param>
/// <param name="A">Pointer x, button index, 1 for the vertical wheel, or a scancode.</param>
/// <param name="B">Pointer y or wheel units, already carrying the sign RDP uses.</param>
internal readonly record struct RdpOperation(RdpOperationKind Kind, int A, int B = 0);
