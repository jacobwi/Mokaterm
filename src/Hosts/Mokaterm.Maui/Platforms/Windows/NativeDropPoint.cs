namespace Mokaterm.Maui.Platforms.Windows;

/// <summary>
/// The arithmetic between an OLE drop callback and the page. A callback is handed a screen point in physical pixels,
/// packed into one 64 bit value; the page hit tests in CSS pixels measured from its own top left corner. Everything in
/// between is here, on its own and without a Windows call in sight, because it is the part that goes wrong silently:
/// a monitor at 100% hides every mistake in it, and a monitor at 150% puts the overlay on the wrong panel.
/// </summary>
internal static class NativeDropPoint
{
	/// <summary>The DPI a CSS pixel is defined at, so at 96 a window pixel and a page pixel are the same size.</summary>
	public const double CssDpi = 96.0;

	/// <summary>
	/// Takes apart the <c>POINTL</c> a drop callback packed into its argument: x in the low half, y in the high half.
	/// Both halves are signed, because a monitor to the left of the primary one has negative screen coordinates.
	/// </summary>
	public static (int X, int Y) ScreenPoint(long point) => ((int)(point & 0xFFFFFFFF), (int)(point >> 32));

	/// <summary>
	/// The page point for a client point in physical pixels. <paramref name="originX"/> and <paramref name="originY"/>
	/// are where the WebView starts inside the window, already in CSS pixels, which is what the page measures from.
	/// </summary>
	public static (double X, double Y) ToCss(int clientX, int clientY, uint dpi, double originX, double originY)
	{
		double scale = ScaleFor(dpi);
		return ((clientX / scale) - originX, (clientY / scale) - originY);
	}

	/// <summary>
	/// How many window pixels one CSS pixel covers. Asked per call and never kept: the window can be dragged onto a
	/// monitor at another scale, and that is exactly what a drag does, so a cached scale would hit test the page at
	/// the size it had on the monitor it came from. A window that cannot report its DPI is read as 100%.
	/// </summary>
	public static double ScaleFor(uint dpi) => dpi > 0 ? dpi / CssDpi : 1.0;
}
