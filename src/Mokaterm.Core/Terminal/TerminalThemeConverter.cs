using System.Text.Json;
using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.Core.Terminal;

/// <summary>
/// Reads and writes the color scheme formats in <see cref="TerminalThemeFormat"/>. Import looks at the content first
/// and only falls back to the file extension, so pasted text works without a file name.
/// </summary>
internal sealed class TerminalThemeConverter : ITerminalThemeConverter
{
	// A Windows Terminal settings.json with every published scheme in it stays well under this.
	private const int MaxTextLength = 1024 * 1024;

	public TerminalThemeImport Import(string text, string? fileName = null)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return TerminalThemeImport.Failure("There is nothing here to import.");
		}

		if (text.Length > MaxTextLength)
		{
			return TerminalThemeImport.Failure("This is too big to be a color scheme (the limit is 1 MB of text).");
		}

		return Detect(text, fileName) switch
		{
			TerminalThemeFormat.WindowsTerminal => WindowsTerminalThemeCodec.Read(text, fileName),
			TerminalThemeFormat.ITerm2 => ITerm2ThemeCodec.Read(text, fileName),
			TerminalThemeFormat.Alacritty => AlacrittyThemeCodec.Read(text, fileName),
			TerminalThemeFormat.Xresources => XresourcesThemeCodec.Read(text, fileName),
			TerminalThemeFormat.VsCode => VsCodeThemeCodec.Read(text, fileName),
			_ => TerminalThemeImport.Failure(
				"This is not a color scheme Mokaterm can read. It understands Windows Terminal schemes, iTerm2 .itermcolors files, Alacritty color tables, X resources and VS Code terminal colors."),
		};
	}

	public string Export(TerminalTheme theme, TerminalThemeFormat format)
	{
		ArgumentNullException.ThrowIfNull(theme);

		return format switch
		{
			TerminalThemeFormat.WindowsTerminal => WindowsTerminalThemeCodec.Write(theme),
			TerminalThemeFormat.ITerm2 => ITerm2ThemeCodec.Write(theme),
			TerminalThemeFormat.Alacritty => AlacrittyThemeCodec.Write(theme),
			TerminalThemeFormat.Xresources => XresourcesThemeCodec.Write(theme),
			TerminalThemeFormat.VsCode => VsCodeThemeCodec.Write(theme),
			_ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown color scheme format."),
		};
	}

	private static TerminalThemeFormat? Detect(string text, string? fileName)
	{
		char first = FirstMeaningful(text);
		if (first == '<')
		{
			return TerminalThemeFormat.ITerm2;
		}

		// A TOML section header also starts with '[', so the Alacritty check has to come before the JSON one.
		if (AlacrittyThemeCodec.IsToml(text))
		{
			return TerminalThemeFormat.Alacritty;
		}

		if (first is '{' or '[')
		{
			return DetectJson(text);
		}

		if (AlacrittyThemeCodec.IsYaml(text))
		{
			return TerminalThemeFormat.Alacritty;
		}

		if (XresourcesThemeCodec.Looks(text))
		{
			return TerminalThemeFormat.Xresources;
		}

		return FromExtension(fileName);
	}

	private static TerminalThemeFormat DetectJson(string text)
	{
		if (!TerminalThemeJson.TryParse(text, out JsonDocument? document, out _))
		{
			// Let the Windows Terminal reader report the parse error, which is the common case for a broken scheme.
			return TerminalThemeFormat.WindowsTerminal;
		}

		using (document)
		{
			return VsCodeThemeCodec.Looks(document.RootElement) ? TerminalThemeFormat.VsCode : TerminalThemeFormat.WindowsTerminal;
		}
	}

	private static TerminalThemeFormat? FromExtension(string? fileName)
	{
		if (string.IsNullOrWhiteSpace(fileName))
		{
			return null;
		}

		string extension = Path.GetExtension(fileName.AsSpan()).ToString();
		if (extension.Length == 0)
		{
			// Files such as .Xresources and Xdefaults carry the name where an extension would be.
			extension = Path.GetFileName(fileName.AsSpan()).ToString();
		}

		return extension.TrimStart('.').ToUpperInvariant() switch
		{
			"ITERMCOLORS" or "PLIST" => TerminalThemeFormat.ITerm2,
			"JSON" or "JSONC" => TerminalThemeFormat.WindowsTerminal,
			"TOML" or "YML" or "YAML" => TerminalThemeFormat.Alacritty,
			"XRESOURCES" or "XDEFAULTS" or "XRDB" or "XCOLORS" => TerminalThemeFormat.Xresources,
			_ => null,
		};
	}

	/// <summary>The first character that is not whitespace or a JSON style comment, which is what gives the format away.</summary>
	private static char FirstMeaningful(string text)
	{
		int index = 0;
		while (index < text.Length)
		{
			char character = text[index];
			if (char.IsWhiteSpace(character))
			{
				index++;
				continue;
			}

			if (character == '/' && index + 1 < text.Length && text[index + 1] == '/')
			{
				int end = text.IndexOf('\n', index);
				if (end < 0)
				{
					return '\0';
				}

				index = end + 1;
				continue;
			}

			if (character == '/' && index + 1 < text.Length && text[index + 1] == '*')
			{
				int end = text.IndexOf("*/", index, StringComparison.Ordinal);
				if (end < 0)
				{
					return '\0';
				}

				index = end + 2;
				continue;
			}

			return character;
		}

		return '\0';
	}
}
