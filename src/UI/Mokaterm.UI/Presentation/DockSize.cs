using System.Globalization;

namespace Mokaterm.UI.Presentation;

/// <summary>
/// The size of a docked panel: pixels in the UI state, a CSS length on the panel, which is also what Moka.Red reports
/// a splitter drag in ("320px").
/// </summary>
internal static class DockSize
{
	private const string Unit = "px";

	public static string Css(double pixels) => string.Create(CultureInfo.InvariantCulture, $"{Math.Round(pixels)}{Unit}");

	public static bool TryPixels(string size, out double pixels)
	{
		pixels = 0;
		return size.EndsWith(Unit, StringComparison.Ordinal)
			&& double.TryParse(size.AsSpan(0, size.Length - Unit.Length), NumberStyles.Float, CultureInfo.InvariantCulture, out pixels);
	}
}
