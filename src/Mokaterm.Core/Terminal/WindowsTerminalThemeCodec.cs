using System.Text.Json;
using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.Core.Terminal;

/// <summary>
/// Windows Terminal color schemes: one scheme object, an array of schemes, or a whole settings.json with a "schemes"
/// array. Windows Terminal calls magenta "purple".
/// </summary>
internal static class WindowsTerminalThemeCodec
{
	/// <summary>Color keys in the order the published schemes list them, so exports look like the files people know.</summary>
	private static readonly string[] SlotNames =
	[
		"black", "red", "green", "yellow", "blue", "purple", "cyan", "white",
		"brightBlack", "brightRed", "brightGreen", "brightYellow", "brightBlue", "brightPurple", "brightCyan", "brightWhite",
	];

	// Alacritty and VS Code spell slot 5 "magenta"; accept that spelling from a hand-written scheme as well.
	private static readonly string[] SlotAliases =
	[
		"", "", "", "", "", "magenta", "", "", "", "", "", "", "", "brightMagenta", "", "",
	];

	public static TerminalThemeImport Read(string text, string? fileName)
	{
		if (!TerminalThemeJson.TryParse(text, out JsonDocument? document, out string? error))
		{
			return TerminalThemeImport.Failure(error);
		}

		using (document)
		{
			if (FindScheme(document.RootElement) is not { } scheme)
			{
				return TerminalThemeImport.Failure("This JSON holds no Windows Terminal color scheme.");
			}

			Dictionary<string, string> values = TerminalThemeJson.StringProperties(scheme);
			TerminalThemeDraft draft = new(TerminalThemeFormat.WindowsTerminal, "a Windows Terminal color scheme", SlotNames);
			draft.SetName(values.GetValueOrDefault("name"));
			draft.SetBackground(values.GetValueOrDefault("background"));
			draft.SetForeground(values.GetValueOrDefault("foreground"));
			draft.SetCursor(values.GetValueOrDefault("cursorColor"));
			draft.SetSelectionBackground(values.GetValueOrDefault("selectionBackground"));
			for (int index = 0; index < TerminalThemeDraft.AnsiCount; index++)
			{
				draft.SetAnsi(index, values.GetValueOrDefault(SlotNames[index]) ?? Alias(values, index));
			}

			return draft.Build(fileName);
		}
	}

	public static string Write(TerminalTheme theme)
	{
		string[] colors =
		[
			theme.Black, theme.Red, theme.Green, theme.Yellow, theme.Blue, theme.Magenta, theme.Cyan, theme.White,
			theme.BrightBlack, theme.BrightRed, theme.BrightGreen, theme.BrightYellow, theme.BrightBlue, theme.BrightMagenta, theme.BrightCyan, theme.BrightWhite,
		];

		return TerminalThemeJson.Write(writer =>
		{
			writer.WriteStartObject();
			writer.WriteString("name", theme.Name);
			for (int index = 0; index < colors.Length; index++)
			{
				writer.WriteString(SlotNames[index], colors[index]);
			}

			writer.WriteString("background", theme.Background);
			writer.WriteString("foreground", theme.Foreground);
			writer.WriteString("cursorColor", theme.Cursor);

			// Windows Terminal takes #rgb and #rrggbb only, so a translucent selection has to be flattened here.
			writer.WriteString("selectionBackground", TerminalColors.Flatten(theme.SelectionBackground, theme.Background));
			writer.WriteEndObject();
		});
	}

	private static string? Alias(Dictionary<string, string> values, int index) =>
		SlotAliases[index].Length == 0 ? null : values.GetValueOrDefault(SlotAliases[index]);

	private static JsonElement? FindScheme(JsonElement root)
	{
		if (root.ValueKind == JsonValueKind.Array)
		{
			return FirstObject(root);
		}

		if (root.ValueKind != JsonValueKind.Object)
		{
			return null;
		}

		if (TerminalThemeJson.Property(root, "schemes") is { ValueKind: JsonValueKind.Array } schemes)
		{
			return FirstObject(schemes);
		}

		return root;
	}

	private static JsonElement? FirstObject(JsonElement array)
	{
		foreach (JsonElement item in array.EnumerateArray())
		{
			if (item.ValueKind == JsonValueKind.Object)
			{
				return item;
			}
		}

		return null;
	}
}
