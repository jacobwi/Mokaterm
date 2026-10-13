namespace Mokaterm.UI.Common.Platform;

/// <summary>
/// Where a tool window sat and how big it was, in the desktop's device-independent units, and whether it stayed on top
/// of other windows.
/// </summary>
public sealed record ToolWindowBounds(double X, double Y, double Width, double Height)
{
	/// <summary>Smaller than this and the tool's own layout breaks, so a saved size below it is not used.</summary>
	public const double MinWidth = 480;

	/// <inheritdoc cref="MinWidth"/>
	public const double MinHeight = 320;

	/// <summary>The window stays above other windows, the main one included.</summary>
	public bool KeepOnTop { get; init; }

	/// <summary>Whether the numbers could be a window at all, which a hand-edited file may not hold.</summary>
	public bool IsUsable =>
		double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Width) && double.IsFinite(Height)
		&& Width >= MinWidth && Height >= MinHeight && Width <= 100_000 && Height <= 100_000;
}
