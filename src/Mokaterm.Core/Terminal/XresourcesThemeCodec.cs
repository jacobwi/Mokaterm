using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.Core.Terminal;

/// <summary>
/// X resources color definitions. Any program prefix is accepted (<c>*.color0</c>, <c>URxvt*color0</c>), and the
/// <c>#define</c> aliases base16 templates lean on are resolved before the values are read.
/// </summary>
internal static partial class XresourcesThemeCodec
{
	private static readonly string[] SlotNames =
	[
		"*.color0", "*.color1", "*.color2", "*.color3", "*.color4", "*.color5", "*.color6", "*.color7",
		"*.color8", "*.color9", "*.color10", "*.color11", "*.color12", "*.color13", "*.color14", "*.color15",
	];

	public static TerminalThemeImport Read(string text, string? fileName)
	{
		Dictionary<string, string> values = ReadResources(text);
		TerminalThemeDraft draft = new(TerminalThemeFormat.Xresources, "an X resources file", SlotNames);
		draft.SetName(TerminalThemeText.NameFromComment(text, "!"));
		draft.SetBackground(values.GetValueOrDefault("background"));
		draft.SetForeground(values.GetValueOrDefault("foreground"));
		draft.SetCursor(values.GetValueOrDefault("cursorColor"));
		draft.SetCursorAccent(values.GetValueOrDefault("cursorColor2"));
		for (int index = 0; index < TerminalThemeDraft.AnsiCount; index++)
		{
			draft.SetAnsi(index, values.GetValueOrDefault("color" + index.ToString(CultureInfo.InvariantCulture)));
		}

		return draft.Build(fileName);
	}

	public static bool Looks(string text)
	{
		int found = 0;
		foreach (string key in ReadResources(text).Keys)
		{
			if (key.StartsWith("color", StringComparison.OrdinalIgnoreCase) || key is "foreground" or "background")
			{
				found++;
			}
		}

		return found >= 4;
	}

	public static string Write(TerminalTheme theme)
	{
		string[] colors =
		[
			theme.Black, theme.Red, theme.Green, theme.Yellow, theme.Blue, theme.Magenta, theme.Cyan, theme.White,
			theme.BrightBlack, theme.BrightRed, theme.BrightGreen, theme.BrightYellow, theme.BrightBlue, theme.BrightMagenta, theme.BrightCyan, theme.BrightWhite,
		];

		StringBuilder builder = new();
		builder.Append("! ").Append(theme.Name).Append('\n');
		Resource(builder, "*.foreground", theme.Foreground);
		Resource(builder, "*.background", theme.Background);
		Resource(builder, "*.cursorColor", theme.Cursor);
		Resource(builder, "*.cursorColor2", theme.CursorAccent);
		builder.Append('\n');
		for (int index = 0; index < colors.Length; index++)
		{
			Resource(builder, SlotNames[index], colors[index]);
		}

		return builder.ToString();
	}

	/// <summary>Every resource keyed by its last name part, with <c>#define</c> aliases already resolved.</summary>
	private static Dictionary<string, string> ReadResources(string text)
	{
		Dictionary<string, string> defines = new(StringComparer.Ordinal);
		Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
		foreach (string line in TerminalThemeText.Lines(text))
		{
			string trimmed = line.Trim();
			if (DefinePattern().Match(trimmed) is { Success: true } define)
			{
				defines[define.Groups["name"].Value] = define.Groups["value"].Value;
			}
			else if (trimmed.Length > 0 && trimmed[0] != '!' && ResourcePattern().Match(trimmed) is { Success: true } resource)
			{
				// xrdb lets a later line override an earlier one, so the last definition wins here too.
				values[resource.Groups["key"].Value] = resource.Groups["value"].Value;
			}
		}

		foreach (string key in values.Keys.ToArray())
		{
			if (TerminalColors.Parse(values[key]) is null && defines.TryGetValue(values[key], out string? alias))
			{
				values[key] = alias;
			}
		}

		return values;
	}

	private static void Resource(StringBuilder builder, string key, string color) =>
		builder.Append(key).Append(':').Append(' ', Math.Max(1, 16 - key.Length)).Append(color).Append('\n');

	[GeneratedRegex(@"^#define\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s+(?<value>\S+)", RegexOptions.CultureInvariant)]
	private static partial Regex DefinePattern();

	[GeneratedRegex(@"^(?:[A-Za-z0-9_*.-]*[*.])?(?<key>[A-Za-z0-9_]+)\s*:\s*(?<value>\S+)", RegexOptions.CultureInvariant)]
	private static partial Regex ResourcePattern();
}
