namespace Mokaterm.UI.Common.Platform;

/// <summary>
/// Where the window's own buttons sit over the top bar, in CSS pixels: the width they cover at each end, and their
/// height, which the bar matches so its controls line up with them.
/// </summary>
public readonly record struct WindowChromeInsets(double Left, double Right, double Height);
