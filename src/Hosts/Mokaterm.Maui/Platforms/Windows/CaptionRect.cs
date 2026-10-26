namespace Mokaterm.Maui.Platforms.Windows;

// Physical pixels of the window's client area, the unit Windows takes caption regions in.
internal readonly record struct CaptionRect(int X, int Y, int Width, int Height);
