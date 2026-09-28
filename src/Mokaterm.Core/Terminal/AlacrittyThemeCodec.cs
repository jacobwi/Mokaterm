using System.Text;
using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.Core.Terminal;

/// <summary>
/// Alacritty color tables in both layouts: the TOML sections used since 0.13 and the older YAML tree. Only the color
/// keys are read, so neither parser has to understand the rest of the configuration.
/// </summary>
internal static class AlacrittyThemeCodec
{
	private static readonly string[] SlotNames =
	[
		"colors.normal.black", "colors.normal.red", "colors.normal.green", "colors.normal.yellow",
		"colors.normal.blue", "colors.normal.magenta", "colors.normal.cyan", "colors.normal.white",
		"colors.bright.black", "colors.bright.red", "colors.bright.green", "colors.bright.yellow",
		"colors.bright.blue", "colors.bright.magenta", "colors.bright.cyan", "colors.bright.white",
	];

	public static TerminalThemeImport Read(string text, string? fileName)
	{
		bool yaml = IsYaml(text);
		TerminalThemeDraft draft = Fill(text, yaml ? ReadYaml(text) : ReadToml(text));
		if (!draft.HasColor)
		{
			// A pasted fragment can be missing the section header or the colors root that tells the layouts apart.
			draft = Fill(text, yaml ? ReadToml(text) : ReadYaml(text));
		}

		return draft.Build(fileName);
	}

	public static bool IsToml(string text)
	{
		foreach (string line in TerminalThemeText.Lines(text))
		{
			string trimmed = line.TrimStart();
			if (trimmed.StartsWith("[colors", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("[\"colors", StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
	}

	public static bool IsYaml(string text)
	{
		bool colors = false;
		foreach (string line in TerminalThemeText.Lines(text))
		{
			string trimmed = TerminalThemeText.StripComment(line, '#').Trim();
			if (!colors)
			{
				colors = trimmed.Equals("colors:", StringComparison.OrdinalIgnoreCase);
			}
			else if (trimmed is "primary:" or "normal:" or "bright:" or "cursor:" or "selection:")
			{
				return true;
			}
		}

		return false;
	}

	public static string Write(TerminalTheme theme)
	{
		string selection = TerminalColors.Flatten(theme.SelectionBackground, theme.Background);
		StringBuilder builder = new();
		builder.Append("# ").Append(theme.Name).Append('\n');
		builder.Append("\n[colors.primary]\n");
		Pair(builder, "background", theme.Background);
		Pair(builder, "foreground", theme.Foreground);
		builder.Append("\n[colors.cursor]\n");
		Pair(builder, "text", theme.CursorAccent);
		Pair(builder, "cursor", theme.Cursor);
		builder.Append("\n[colors.selection]\n");
		if (theme.SelectionForeground is { } selectionForeground)
		{
			Pair(builder, "text", selectionForeground);
		}

		Pair(builder, "background", selection);
		builder.Append("\n[colors.normal]\n");
		Pair(builder, "black", theme.Black);
		Pair(builder, "red", theme.Red);
		Pair(builder, "green", theme.Green);
		Pair(builder, "yellow", theme.Yellow);
		Pair(builder, "blue", theme.Blue);
		Pair(builder, "magenta", theme.Magenta);
		Pair(builder, "cyan", theme.Cyan);
		Pair(builder, "white", theme.White);
		builder.Append("\n[colors.bright]\n");
		Pair(builder, "black", theme.BrightBlack);
		Pair(builder, "red", theme.BrightRed);
		Pair(builder, "green", theme.BrightGreen);
		Pair(builder, "yellow", theme.BrightYellow);
		Pair(builder, "blue", theme.BrightBlue);
		Pair(builder, "magenta", theme.BrightMagenta);
		Pair(builder, "cyan", theme.BrightCyan);
		Pair(builder, "white", theme.BrightWhite);
		return builder.ToString();
	}

	private static TerminalThemeDraft Fill(string text, Dictionary<string, string> values)
	{
		TerminalThemeDraft draft = new(TerminalThemeFormat.Alacritty, "an Alacritty color table", SlotNames);
		draft.SetName(TerminalThemeText.NameFromComment(text, "#"));
		draft.SetBackground(Value(values, "primary.background"));
		draft.SetForeground(Value(values, "primary.foreground"));
		draft.SetCursor(Value(values, "cursor.cursor"));
		draft.SetCursorAccent(Value(values, "cursor.text"));
		draft.SetSelectionBackground(Value(values, "selection.background"));
		draft.SetSelectionForeground(Value(values, "selection.text"));
		for (int index = 0; index < TerminalThemeDraft.AnsiCount; index++)
		{
			draft.SetAnsi(index, Value(values, SlotNames[index]) ?? Alias(values, index));
		}

		return draft;
	}

	/// <summary>Reads <c>[colors.x]</c> sections, dotted keys and one level of inline tables into one flat map.</summary>
	private static Dictionary<string, string> ReadToml(string text)
	{
		Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
		string section = "";
		foreach (string line in TerminalThemeText.Lines(text))
		{
			string trimmed = TerminalThemeText.StripComment(line, '#').Trim();
			if (trimmed.Length == 0)
			{
				continue;
			}

			if (trimmed[0] == '[' && trimmed[^1] == ']')
			{
				section = trimmed[1..^1].Trim().Replace("\"", "", StringComparison.Ordinal);
				continue;
			}

			if (!TerminalThemeText.TrySplit(trimmed, '=', out string key, out string value))
			{
				continue;
			}

			string path = section.Length == 0 ? key : section + "." + key;
			if (value.StartsWith('{'))
			{
				foreach (string part in value.Trim('{', '}').Split(','))
				{
					if (TerminalThemeText.TrySplit(part, '=', out string innerKey, out string innerValue))
					{
						values[path + "." + innerKey] = innerValue;
					}
				}
			}
			else
			{
				values[path] = value;
			}
		}

		return values;
	}

	/// <summary>Reads the nested YAML layout by indentation, which is all the color keys need.</summary>
	private static Dictionary<string, string> ReadYaml(string text)
	{
		Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
		List<(int Indent, string Key)> path = [];
		foreach (string line in TerminalThemeText.Lines(text))
		{
			string content = TerminalThemeText.StripComment(line.TrimEnd(), '#');
			string trimmed = content.Trim();
			if (trimmed.Length == 0 || !TerminalThemeText.TrySplit(trimmed, ':', out string key, out string value))
			{
				continue;
			}

			int indent = content.Length - content.TrimStart().Length;
			while (path.Count > 0 && path[^1].Indent >= indent)
			{
				path.RemoveAt(path.Count - 1);
			}

			if (value.Length == 0)
			{
				path.Add((indent, key));
			}
			else
			{
				values[string.Join('.', path.Select(part => part.Key).Append(key))] = value;
			}
		}

		return values;
	}

	private static string? Alias(Dictionary<string, string> values, int index) => index switch
	{
		5 => Value(values, "normal.purple"),
		13 => Value(values, "bright.purple"),
		_ => null,
	};

	// A pasted fragment may start at "normal.black" rather than the full "colors.normal.black" path.
	private static string? Value(Dictionary<string, string> values, string path)
	{
		string suffix = path.StartsWith("colors.", StringComparison.OrdinalIgnoreCase) ? path["colors.".Length..] : path;
		return values.GetValueOrDefault("colors." + suffix) ?? values.GetValueOrDefault(suffix);
	}

	private static void Pair(StringBuilder builder, string key, string color) =>
		builder.Append(key).Append(" = \"").Append(color).Append("\"\n");
}
