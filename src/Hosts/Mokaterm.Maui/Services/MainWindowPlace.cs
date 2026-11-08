using System.Globalization;

namespace Mokaterm.Maui.Services;

/// <summary>
/// Where the main window was when it closed, in the screen's own pixels. Pixels and not device-independent units, since
/// units only mean something against the scale of a screen, and at the next start there is no window yet to ask.
/// </summary>
internal readonly record struct MainWindowPlace(int X, int Y, int Width, int Height, bool Maximized)
{
	// Windows' own limit on a window's side.
	private const int MaxSide = 32767;

	public string Write() => string.Create(CultureInfo.InvariantCulture, $"{X},{Y},{Width},{Height},{(Maximized ? 1 : 0)}");

	/// <summary>The place in <paramref name="text"/>, or null for anything <see cref="Write"/> would not have written.</summary>
	public static MainWindowPlace? Read(string? text)
	{
		string[] parts = (text ?? "").Trim().Split(',');
		if (parts.Length == 5
			&& TryInt(parts[0], out int x)
			&& TryInt(parts[1], out int y)
			&& TryInt(parts[2], out int width)
			&& TryInt(parts[3], out int height)
			&& parts[4] is "0" or "1"
			&& width is > 0 and <= MaxSide
			&& height is > 0 and <= MaxSide)
		{
			return new MainWindowPlace(x, y, width, height, parts[4] == "1");
		}

		return null;
	}

	private static bool TryInt(string text, out int value) =>
		int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
}
