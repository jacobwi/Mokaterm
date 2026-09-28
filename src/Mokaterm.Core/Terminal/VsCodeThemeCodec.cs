using System.Text.Json;
using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.Core.Terminal;

/// <summary>
/// VS Code terminal colors: the <c>workbench.colorCustomizations</c> block of a settings file, a theme-scoped block
/// inside it, or the customizations object on its own.
/// </summary>
internal static class VsCodeThemeCodec
{
	private const string CustomizationsKey = "workbench.colorCustomizations";

	private static readonly string[] SlotNames =
	[
		"terminal.ansiBlack", "terminal.ansiRed", "terminal.ansiGreen", "terminal.ansiYellow",
		"terminal.ansiBlue", "terminal.ansiMagenta", "terminal.ansiCyan", "terminal.ansiWhite",
		"terminal.ansiBrightBlack", "terminal.ansiBrightRed", "terminal.ansiBrightGreen", "terminal.ansiBrightYellow",
		"terminal.ansiBrightBlue", "terminal.ansiBrightMagenta", "terminal.ansiBrightCyan", "terminal.ansiBrightWhite",
	];

	public static TerminalThemeImport Read(string text, string? fileName)
	{
		if (!TerminalThemeJson.TryParse(text, out JsonDocument? document, out string? error))
		{
			return TerminalThemeImport.Failure(error);
		}

		using (document)
		{
			Dictionary<string, string> values = TerminalThemeJson.StringProperties(Customizations(document.RootElement));
			TerminalThemeDraft draft = new(TerminalThemeFormat.VsCode, "VS Code terminal colors", SlotNames);
			draft.SetName(TerminalThemeText.NameFromComment(text, "//") ?? ThemeName(document.RootElement));
			draft.SetBackground(values.GetValueOrDefault("terminal.background"));
			draft.SetForeground(values.GetValueOrDefault("terminal.foreground"));
			draft.SetCursor(values.GetValueOrDefault("terminalCursor.foreground"));
			draft.SetCursorAccent(values.GetValueOrDefault("terminalCursor.background"));
			draft.SetSelectionBackground(values.GetValueOrDefault("terminal.selectionBackground"));
			draft.SetSelectionForeground(values.GetValueOrDefault("terminal.selectionForeground"));
			for (int index = 0; index < TerminalThemeDraft.AnsiCount; index++)
			{
				draft.SetAnsi(index, values.GetValueOrDefault(SlotNames[index]));
			}

			return draft.Build(fileName);
		}
	}

	/// <summary>True when the JSON carries the terminal keys VS Code uses.</summary>
	public static bool Looks(JsonElement root) =>
		TerminalThemeJson.Property(root, CustomizationsKey) is not null
		|| TerminalThemeJson.StringProperties(Customizations(root)).Keys.Any(static key => key.StartsWith("terminal.", StringComparison.OrdinalIgnoreCase));

	public static string Write(TerminalTheme theme)
	{
		string[] colors =
		[
			theme.Black, theme.Red, theme.Green, theme.Yellow, theme.Blue, theme.Magenta, theme.Cyan, theme.White,
			theme.BrightBlack, theme.BrightRed, theme.BrightGreen, theme.BrightYellow, theme.BrightBlue, theme.BrightMagenta, theme.BrightCyan, theme.BrightWhite,
		];

		// settings.json takes comments, so the name survives a round trip through this format.
		return "// " + theme.Name + "\n" + TerminalThemeJson.Write(writer =>
		{
			writer.WriteStartObject();
			writer.WriteStartObject(CustomizationsKey);
			writer.WriteString("terminal.background", theme.Background);
			writer.WriteString("terminal.foreground", theme.Foreground);
			writer.WriteString("terminalCursor.foreground", theme.Cursor);
			writer.WriteString("terminalCursor.background", theme.CursorAccent);
			writer.WriteString("terminal.selectionBackground", theme.SelectionBackground);
			if (theme.SelectionForeground is { } selectionForeground)
			{
				writer.WriteString("terminal.selectionForeground", selectionForeground);
			}

			for (int index = 0; index < colors.Length; index++)
			{
				writer.WriteString(SlotNames[index], colors[index]);
			}

			writer.WriteEndObject();
			writer.WriteEndObject();
		});
	}

	/// <summary>The object holding the terminal keys: the customizations block, a theme scope inside it, or the root.</summary>
	private static JsonElement Customizations(JsonElement root)
	{
		JsonElement block = TerminalThemeJson.Property(root, CustomizationsKey) ?? root;
		if (block.ValueKind != JsonValueKind.Object || HasTerminalKey(block))
		{
			return block;
		}

		// Customizations may be scoped per theme: "[Dracula]": { "terminal.background": ... }.
		foreach (JsonProperty property in block.EnumerateObject())
		{
			if (property.Value.ValueKind == JsonValueKind.Object && HasTerminalKey(property.Value))
			{
				return property.Value;
			}
		}

		return block;
	}

	private static bool HasTerminalKey(JsonElement element)
	{
		foreach (JsonProperty property in element.EnumerateObject())
		{
			if (property.Value.ValueKind == JsonValueKind.String && property.Name.StartsWith("terminal", StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
	}

	private static string? ThemeName(JsonElement root) =>
		TerminalThemeJson.Property(root, "workbench.colorTheme") is { ValueKind: JsonValueKind.String } theme ? theme.GetString() : null;
}
