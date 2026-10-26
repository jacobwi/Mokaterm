using Mokaterm.UI.Common.Platform;

namespace Mokaterm.Maui.Platforms.Windows;

// Kept apart from the Windows calls so it can be checked at every scale: a mistake here puts a caption region over a
// control, and Windows then takes the click before the page sees it.
internal static class CaptionRegions
{
	public static WindowChromeInsets Insets(int leftInset, int rightInset, int height, double scale)
	{
		double usable = Usable(scale);
		return new WindowChromeInsets(leftInset / usable, rightInset / usable, height / usable);
	}

	// For a page that shows no bar: the strip a system title bar would take, between the window's buttons.
	public static CaptionRect[] Default(int clientWidth, int leftInset, int rightInset, int height)
	{
		int width = clientWidth - leftInset - rightInset;
		return width > 0 && height > 0 ? [new CaptionRect(leftInset, 0, width, height)] : [];
	}

	// The origin is where the WebView starts in the window, in view pixels, which are the page's own while page zoom is
	// off. No rectangle reaches under the caption buttons.
	public static CaptionRect[] FromPage(
		IReadOnlyList<WindowDragRegion> regions,
		double scale,
		double originX,
		double originY,
		int clientWidth,
		int leftInset,
		int rightInset)
	{
		ArgumentNullException.ThrowIfNull(regions);
		double usable = Usable(scale);
		int maximumRight = clientWidth - rightInset;
		List<CaptionRect> rects = new(regions.Count);
		foreach (WindowDragRegion region in regions)
		{
			if (!IsUsable(region))
			{
				continue;
			}

			// Rounded inwards: one pixel too many would take a click meant for the control beside it.
			int left = Math.Max(leftInset, (int)Math.Ceiling((originX + region.X) * usable));
			int top = Math.Max(0, (int)Math.Ceiling((originY + region.Y) * usable));
			int right = Math.Min(maximumRight, (int)Math.Floor((originX + region.X + region.Width) * usable));
			int bottom = (int)Math.Floor((originY + region.Y + region.Height) * usable);
			if (right > left && bottom > top)
			{
				rects.Add(new CaptionRect(left, top, right - left, bottom - top));
			}
		}

		return [.. rects];
	}

	// The page's numbers come from script; a rectangle that is not a real one is dropped rather than trusted.
	private static bool IsUsable(WindowDragRegion region) =>
		double.IsFinite(region.X) && double.IsFinite(region.Y) && double.IsFinite(region.Width) && double.IsFinite(region.Height)
		&& region.Width > 0 && region.Height > 0;

	// A window that cannot say its scale yet is read as 100%, rather than dividing by nothing.
	private static double Usable(double scale) => double.IsFinite(scale) && scale > 0 ? scale : 1.0;
}
