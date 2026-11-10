using System.Globalization;
using ColorHelper = Microsoft.UI.ColorHelper;
using WinColor = Windows.UI.Color;

namespace Mokaterm.Maui.Platforms.Windows;

internal static class WindowColors
{
	// Moka's palette entries are #rrggbb; anything else is null, which leaves the system's colour in place.
	public static WinColor? FromHex(string value)
	{
		if (value.Length == 7 && value[0] == '#'
			&& uint.TryParse(value.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint rgb))
		{
			return ColorHelper.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
		}

		return null;
	}
}
