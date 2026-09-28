using System.Globalization;
using System.Text;

namespace Mokaterm.DevHost.Demo.Terminal;

/// <summary>
/// How many terminal columns text takes: two for wide East Asian characters and most emoji, none for combining marks and
/// joiners. Close enough to <c>wcwidth</c> for lining up <c>ls</c> columns.
/// </summary>
internal static class TerminalCells
{
	public static int Width(string text)
	{
		int width = 0;
		foreach (Rune rune in text.EnumerateRunes())
		{
			width += Width(rune);
		}

		return width;
	}

	private static int Width(Rune rune)
	{
		UnicodeCategory category = Rune.GetUnicodeCategory(rune);
		if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format)
		{
			return 0;
		}

		return rune.Value switch
		{
			>= 0xFE00 and <= 0xFE0F => 0,
			>= 0x1100 and <= 0x115F => 2,
			>= 0x2E80 and <= 0xA4CF => 2,
			>= 0xAC00 and <= 0xD7A3 => 2,
			>= 0xF900 and <= 0xFAFF => 2,
			>= 0xFE30 and <= 0xFE4F => 2,
			>= 0xFF00 and <= 0xFF60 => 2,
			>= 0xFFE0 and <= 0xFFE6 => 2,
			>= 0x1F300 and <= 0x1FAFF => 2,
			>= 0x20000 and <= 0x3FFFD => 2,
			_ => 1,
		};
	}
}
