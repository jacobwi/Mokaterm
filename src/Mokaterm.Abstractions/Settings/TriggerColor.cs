namespace Mokaterm.Abstractions.Settings;

/// <summary>
/// The colors a trigger can paint a match in: the app's own status colors, so a highlight means the same thing in every
/// theme. The colors themselves belong to the UI; this only names them.
/// </summary>
public enum TriggerColor
{
	/// <summary>The match keeps whatever color the server gave it.</summary>
	None,

	Red,

	Orange,

	Yellow,

	Green,

	Blue,

	Purple,
}
