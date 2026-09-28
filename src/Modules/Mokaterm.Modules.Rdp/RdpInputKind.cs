namespace Mokaterm.Modules.Rdp;

/// <summary>
/// What one event from the page means. The numbers are part of the contract with <c>rdp.js</c>, which sends them
/// as they are, so they never change.
/// </summary>
public enum RdpInputKind
{
	/// <summary>Pointer moved to A, B in remote pixels.</summary>
	MouseMove = 0,

	/// <summary>Button A pressed: 0 left, 1 middle, 2 right, 3 and 4 the side buttons.</summary>
	MouseDown = 1,

	MouseUp = 2,

	/// <summary>Wheel turned: A is 1 for the vertical wheel and 0 for the horizontal one, B the units as the page reports them.</summary>
	Wheel = 3,

	/// <summary>Key down, with <see cref="RdpInputEvent.Code"/> holding a <c>KeyboardEvent.code</c> such as <c>KeyA</c>.</summary>
	KeyDown = 4,

	KeyUp = 5,

	/// <summary>The screen lost focus or went away: everything still held goes up.</summary>
	ReleaseKeys = 6,
}
