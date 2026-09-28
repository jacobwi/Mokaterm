namespace Mokaterm.UI.Common.Platform;

/// <summary>
/// A host window without a system title bar: the shell's top bar is drawn where the title bar would be and takes over
/// its jobs. The desktop host implements it. A host that registers nothing keeps the top bar an ordinary bar.
/// </summary>
public interface IWindowChrome
{
	/// <summary>Where the window's own buttons sit over the top bar, in CSS pixels.</summary>
	WindowChromeInsets Insets { get; }

	/// <summary>Raised when <see cref="Insets"/> changes, for example on a screen with another scale. May fire on any thread.</summary>
	event Action? InsetsChanged;

	/// <summary>
	/// Sets the parts of the top bar that act as the title bar: dragging one moves the window, a double click maximizes it
	/// and a right click opens the window menu. An empty list leaves nothing to drag, which is right while a dropdown in
	/// the bar waits for the click that closes it.
	/// </summary>
	void SetDragRegions(IReadOnlyList<WindowDragRegion> regions);

	/// <summary>The page shows no top bar (starting up, locked): the window falls back to a title bar strip of its own.</summary>
	void ResetDragRegions();
}
