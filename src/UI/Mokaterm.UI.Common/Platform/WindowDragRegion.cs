namespace Mokaterm.UI.Common.Platform;

/// <summary>A rectangle of the page that moves the window, in CSS pixels from the page's top left corner.</summary>
public readonly record struct WindowDragRegion(double X, double Y, double Width, double Height);
