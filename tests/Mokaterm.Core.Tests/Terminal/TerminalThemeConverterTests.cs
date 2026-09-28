using Microsoft.Extensions.DependencyInjection;
using Mokaterm.Abstractions.Storage;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.Core.Terminal;
using Mokaterm.Core.Tests.TestSupport;

namespace Mokaterm.Core.Tests.Terminal;

public sealed class TerminalThemeConverterTests
{
	private static readonly TerminalThemeConverter Converter = new();

	[Fact]
	public async Task Converter_IsRegisteredAsASingleton()
	{
		await using CoreTestContext context = CreateContext();

		ITerminalThemeConverter converter = context.Services.GetRequiredService<ITerminalThemeConverter>();

		Assert.IsType<TerminalThemeConverter>(converter);
		Assert.Same(converter, context.Services.GetRequiredService<ITerminalThemeConverter>());
	}

	[Theory]
	[InlineData(TerminalThemeFormat.WindowsTerminal)]
	[InlineData(TerminalThemeFormat.ITerm2)]
	[InlineData(TerminalThemeFormat.Alacritty)]
	[InlineData(TerminalThemeFormat.Xresources)]
	[InlineData(TerminalThemeFormat.VsCode)]
	public void Export_ThenImport_KeepsTheColorsTheFormatCarries(TerminalThemeFormat format)
	{
		TerminalTheme theme = Sample();

		string text = Converter.Export(theme, format);

		// An iTerm2 file has nowhere to put the name, so there it comes from the file the user saved.
		TerminalThemeImport import = Converter.Import(text, theme.Name + Extension(format));

		Assert.True(import.Succeeded, import.Error);
		Assert.Equal(format, import.Format);
		TerminalTheme result = import.Theme!;
		Assert.Equal(theme.Name, result.Name);
		Assert.Equal(Palette(theme), Palette(result));
		Assert.Equal(theme.Background, result.Background);
		Assert.Equal(theme.Foreground, result.Foreground);
		Assert.Equal(theme.Cursor, result.Cursor);
		Assert.Equal(theme.IsDark, result.IsDark);
		Assert.True(result.IsCustom);
		Assert.NotEqual(theme.Id, result.Id);

		// Only Windows Terminal has no place for the cursor text color.
		Assert.Equal(format == TerminalThemeFormat.WindowsTerminal ? theme.Background : theme.CursorAccent, result.CursorAccent);

		Assert.Equal(format switch
		{
			// X resources carry no selection, so it comes back derived from the foreground.
			TerminalThemeFormat.Xresources => theme.Foreground + "40",
			TerminalThemeFormat.ITerm2 or TerminalThemeFormat.VsCode => theme.SelectionBackground,
			_ => TerminalColors.Flatten(theme.SelectionBackground, theme.Background),
		}, result.SelectionBackground);

		Assert.Equal(
			format is TerminalThemeFormat.ITerm2 or TerminalThemeFormat.VsCode or TerminalThemeFormat.Alacritty ? theme.SelectionForeground : null,
			result.SelectionForeground);
	}

	[Theory]
	[InlineData(TerminalThemeFormat.WindowsTerminal)]
	[InlineData(TerminalThemeFormat.ITerm2)]
	[InlineData(TerminalThemeFormat.Alacritty)]
	[InlineData(TerminalThemeFormat.Xresources)]
	[InlineData(TerminalThemeFormat.VsCode)]
	public void Export_ThenImport_WorksForEveryBuiltInTheme(TerminalThemeFormat format)
	{
		foreach (TerminalTheme theme in BuiltInTerminalThemes.All)
		{
			TerminalThemeImport import = Converter.Import(Converter.Export(theme, format), theme.Id);

			Assert.True(import.Succeeded, theme.Id + ": " + import.Error);
			Assert.Equal(Palette(theme), Palette(import.Theme!));
			Assert.Equal(theme.IsDark, import.Theme!.IsDark);
		}
	}

	[Fact]
	public void Export_UnknownFormat_Throws() =>
		Assert.Throws<ArgumentOutOfRangeException>(() => Converter.Export(Sample(), (TerminalThemeFormat)42));

	[Fact]
	public void Export_WindowsTerminal_WritesPurpleAndAnOpaqueSelection()
	{
		string text = Converter.Export(Sample(), TerminalThemeFormat.WindowsTerminal);

		Assert.Contains("\"purple\": \"#b48ead\"", text, StringComparison.Ordinal);
		Assert.Contains("\"selectionBackground\": \"#223c5f\"", text, StringComparison.Ordinal);
		Assert.DoesNotContain("magenta", text, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void Export_ITerm2_WritesAPropertyList()
	{
		string text = Converter.Export(Sample(), TerminalThemeFormat.ITerm2);

		Assert.StartsWith("<?xml version=\"1.0\" encoding=\"UTF-8\"?>", text, StringComparison.Ordinal);
		Assert.Contains("<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\"", text, StringComparison.Ordinal);
		Assert.Contains("<key>Ansi 15 Color</key>", text, StringComparison.Ordinal);
		Assert.Contains("<string>sRGB</string>", text, StringComparison.Ordinal);
	}

	[Fact]
	public void Export_Alacritty_WritesTheColorSections()
	{
		string text = Converter.Export(Sample(), TerminalThemeFormat.Alacritty);

		Assert.StartsWith("# Sample Theme", text, StringComparison.Ordinal);
		Assert.Contains("[colors.primary]", text, StringComparison.Ordinal);
		Assert.Contains("[colors.bright]", text, StringComparison.Ordinal);
		Assert.Contains("magenta = \"#b48ead\"", text, StringComparison.Ordinal);
	}

	[Fact]
	public void Export_Xresources_WritesOneResourcePerColor()
	{
		string text = Converter.Export(Sample(), TerminalThemeFormat.Xresources);

		Assert.StartsWith("! Sample Theme", text, StringComparison.Ordinal);
		Assert.Contains("*.background:    #101214", text, StringComparison.Ordinal);
		Assert.Contains("*.color15:       #f5f7fa", text, StringComparison.Ordinal);
	}

	[Fact]
	public void Export_VsCode_WritesCustomizationsThatCanBePasted()
	{
		string text = Converter.Export(Sample(), TerminalThemeFormat.VsCode);

		Assert.StartsWith("// Sample Theme", text, StringComparison.Ordinal);
		Assert.Contains("\"workbench.colorCustomizations\"", text, StringComparison.Ordinal);
		Assert.Contains("\"terminal.ansiBrightWhite\": \"#f5f7fa\"", text, StringComparison.Ordinal);
		Assert.Contains("\"terminal.selectionBackground\": \"#3366aa80\"", text, StringComparison.Ordinal);
	}

	[Fact]
	public void Import_WindowsTerminalScheme_ReadsThePublishedDracula()
	{
		TerminalThemeImport import = Converter.Import(TerminalThemeSamples.WindowsTerminalDracula, "Dracula.json");

		TerminalTheme theme = Succeeded(import, TerminalThemeFormat.WindowsTerminal);
		Assert.Equal("Dracula", theme.Name);
		Assert.StartsWith("custom-", theme.Id, StringComparison.Ordinal);
		Assert.True(theme.IsCustom);
		Assert.True(theme.IsDark);
		Assert.Equal("#282a36", theme.Background);
		Assert.Equal("#f8f8f2", theme.Foreground);
		Assert.Equal("#f8f8f2", theme.Cursor);
		Assert.Equal("#282a36", theme.CursorAccent);
		Assert.Equal("#44475a", theme.SelectionBackground);
		Assert.Equal("#ff79c6", theme.Magenta);
		Assert.Equal("#ff92df", theme.BrightMagenta);
		Assert.Equal(Dracula, Palette(theme));
	}

	[Fact]
	public void Import_ITerm2ColorsFile_ReadsTheComponentsBack()
	{
		TerminalThemeImport import = Converter.Import(TerminalThemeSamples.ITerm2Dracula, "Dracula.itermcolors");

		TerminalTheme theme = Succeeded(import, TerminalThemeFormat.ITerm2);
		Assert.Equal("Dracula", theme.Name);
		Assert.Equal(Dracula, Palette(theme));
		Assert.Equal("#282a36", theme.Background);
		Assert.Equal("#f8f8f2", theme.Foreground);
		Assert.Equal("#f8f8f2", theme.Cursor);
		Assert.Equal("#282a36", theme.CursorAccent);
		Assert.Equal("#44475a", theme.SelectionBackground);
		Assert.Equal("#ffffff", theme.SelectionForeground);
	}

	[Fact]
	public void Import_AlacrittyToml_ReadsTheColorSections()
	{
		TerminalThemeImport import = Converter.Import(TerminalThemeSamples.AlacrittyTomlDracula, "Dracula.toml");

		TerminalTheme theme = Succeeded(import, TerminalThemeFormat.Alacritty);
		Assert.Equal("Dracula", theme.Name);
		Assert.Equal(Dracula, Palette(theme));
		Assert.Equal("#282a36", theme.Background);
		Assert.Equal("#f8f8f2", theme.Cursor);
		Assert.Equal("#282a36", theme.CursorAccent);
		Assert.Equal("#44475a", theme.SelectionBackground);
		Assert.Equal("#ffffff", theme.SelectionForeground);
	}

	[Fact]
	public void Import_AlacrittyToml_IgnoresColorsMentionedInComments()
	{
		TerminalThemeImport import = Converter.Import(TerminalThemeSamples.AlacrittyTomlGruvbox);

		TerminalTheme theme = Succeeded(import, TerminalThemeFormat.Alacritty);
		Assert.Equal("Gruvbox dark", theme.Name);

		// The commented out lines offer #1d2021 and #32302f for the background.
		Assert.Equal("#282828", theme.Background);
		Assert.Equal("#ebdbb2", theme.Foreground);
		Assert.Equal("#b16286", theme.Magenta);
	}

	[Fact]
	public void Import_AlacrittyYaml_ReadsTheOlderLayout()
	{
		TerminalThemeImport import = Converter.Import(TerminalThemeSamples.AlacrittyYamlGruvbox, "gruvbox_dark.yaml");

		TerminalTheme theme = Succeeded(import, TerminalThemeFormat.Alacritty);
		Assert.Equal("Gruvbox dark", theme.Name);
		Assert.Equal("#282828", theme.Background);
		Assert.Equal("#ebdbb2", theme.Foreground);
		Assert.Equal("#cc241d", theme.Red);
		Assert.Equal("#a89984", theme.White);
		Assert.Equal("#928374", theme.BrightBlack);
		Assert.Equal("#ebdbb2", theme.BrightWhite);

		// The layout has no cursor or selection, so both are derived.
		Assert.Equal("#ebdbb2", theme.Cursor);
		Assert.Equal("#282828", theme.CursorAccent);
		Assert.Equal("#ebdbb240", theme.SelectionBackground);
	}

	[Fact]
	public void Import_AlacrittyFragmentWithoutTheColorsRoot_StillReads()
	{
		// What someone gets by copying the colors out of an alacritty.yml without the enclosing "colors:" key.
		string fragment = """
			primary:
			  background: '0x282828'
			  foreground: '0xebdbb2'
			normal:
			  black:   '0x282828'
			  red:     '0xcc241d'
			  green:   '0x98971a'
			  yellow:  '0xd79921'
			  blue:    '0x458588'
			  magenta: '0xb16286'
			  cyan:    '0x689d6a'
			  white:   '0xa89984'
			""";

		TerminalThemeImport import = Converter.Import(fragment, "gruvbox.yml");

		TerminalTheme theme = Succeeded(import, TerminalThemeFormat.Alacritty);
		Assert.Equal("gruvbox", theme.Name);
		Assert.Equal("#282828", theme.Background);
		Assert.Equal("#b16286", theme.Magenta);
	}

	[Fact]
	public void Import_Xresources_ReadsAPublishedFileAndItsTitle()
	{
		TerminalThemeImport import = Converter.Import(TerminalThemeSamples.XresourcesDracula);

		TerminalTheme theme = Succeeded(import, TerminalThemeFormat.Xresources);
		Assert.Equal("Dracula", theme.Name);
		Assert.Equal(Dracula, Palette(theme));
		Assert.Equal("#282a36", theme.Background);
		Assert.Equal("#f8f8f2", theme.Cursor);
		Assert.Equal("#f8f8f240", theme.SelectionBackground);
	}

	[Fact]
	public void Import_Xresources_ResolvesDefineAliases()
	{
		TerminalThemeImport import = Converter.Import(TerminalThemeSamples.XresourcesBase16Gruvbox);

		TerminalTheme theme = Succeeded(import, TerminalThemeFormat.Xresources);
		Assert.Equal("base16 Gruvbox dark, hard", theme.Name);
		Assert.Equal("#1d2021", theme.Background);
		Assert.Equal("#d5c4a1", theme.Foreground);
		Assert.Equal("#d5c4a1", theme.Cursor);
		Assert.Equal("#1d2021", theme.Black);
		Assert.Equal("#fb4934", theme.Red);
		Assert.Equal("#665c54", theme.BrightBlack);
		Assert.Equal("#fbf1c7", theme.BrightWhite);
	}

	[Fact]
	public void Import_VsCodeCustomizations_ReadsTheTerminalKeys()
	{
		TerminalThemeImport import = Converter.Import(TerminalThemeSamples.VsCodeDracula, "Dracula.json");

		TerminalTheme theme = Succeeded(import, TerminalThemeFormat.VsCode);
		Assert.Equal("Dracula", theme.Name);
		Assert.Equal(Dracula, Palette(theme));
		Assert.Equal("#282a36", theme.Background);
		Assert.Equal("#f8f8f2", theme.Cursor);
		Assert.Equal("#282a36", theme.CursorAccent);
		Assert.Equal("#44475a", theme.SelectionBackground);
	}

	[Fact]
	public void Import_VsCodeSettings_ReadsAThemeScopedBlockAndItsName()
	{
		string settings = """
			{
				// The terminal colors sit under a theme scope here.
				"workbench.colorTheme": "Night Owl",
				"workbench.colorCustomizations": {
					"[Night Owl]": {
						"terminal.background": "#011627",
						"terminal.foreground": "#d6deeb",
						"terminal.ansiBlack": "#011627",
						"terminal.ansiRed": "#ef5350",
						"terminal.ansiGreen": "#22da6e",
						"terminal.ansiYellow": "#addb67",
						"terminal.ansiBlue": "#82aaff",
						"terminal.ansiMagenta": "#c792ea",
						"terminal.ansiCyan": "#21c7a8",
						"terminal.ansiWhite": "#ffffff",
					},
				},
			}
			""";

		TerminalThemeImport import = Converter.Import(settings, "settings.json");

		TerminalTheme theme = Succeeded(import, TerminalThemeFormat.VsCode);
		Assert.Equal("Night Owl", theme.Name);
		Assert.Equal("#011627", theme.Background);
		Assert.Equal("#c792ea", theme.Magenta);
	}

	[Fact]
	public void Import_WindowsTerminalSettings_TakesTheFirstScheme()
	{
		string settings = """
			// Windows Terminal settings, comments and trailing commas included.
			{
				"$help": "https://aka.ms/terminal-documentation",
				"defaultProfile": "{61c54bbd-c2c6-5271-96e7-009a87ff44bf}",
				"schemes": [
					{
						"name": "Tomorrow Night",
						"background": "#1d1f21",
						"foreground": "#c5c8c6",
						"cursorColor": "#c5c8c6",
						"selectionBackground": "#373b41",
						"black": "#000000",
						"red": "#cc6666",
						"green": "#b5bd68",
						"yellow": "#f0c674",
						"blue": "#81a2be",
						"purple": "#b294bb",
						"cyan": "#8abeb7",
						"white": "#ffffff",
						"brightBlack": "#4c4c4c",
						"brightRed": "#cc6666",
						"brightGreen": "#b5bd68",
						"brightYellow": "#f0c674",
						"brightBlue": "#81a2be",
						"brightPurple": "#b294bb",
						"brightCyan": "#8abeb7",
						"brightWhite": "#ffffff",
					},
					{
						"name": "Campbell",
						"background": "#0c0c0c",
						"foreground": "#cccccc",
						"black": "#0c0c0c",
						"red": "#c50f1f",
						"green": "#13a10e",
						"yellow": "#c19c00",
						"blue": "#0037da",
						"purple": "#881798",
						"cyan": "#3a96dd",
						"white": "#cccccc",
					},
				],
			}
			""";

		TerminalThemeImport import = Converter.Import(settings, "settings.json");

		TerminalTheme theme = Succeeded(import, TerminalThemeFormat.WindowsTerminal);
		Assert.Equal("Tomorrow Night", theme.Name);
		Assert.Equal("#1d1f21", theme.Background);
		Assert.Equal("#373b41", theme.SelectionBackground);
	}

	[Fact]
	public void Import_SchemeArray_TakesTheFirstScheme()
	{
		string schemes = "[ " + TerminalThemeSamples.WindowsTerminalDracula + ", { \"name\": \"Second\" } ]";

		TerminalThemeImport import = Converter.Import(schemes);

		Assert.Equal("Dracula", Succeeded(import, TerminalThemeFormat.WindowsTerminal).Name);
	}

	[Fact]
	public void Import_MissingColors_DerivesThemFromWhatIsThere()
	{
		string scheme = """
			{
				"name": "Half A Scheme",
				"black": "#000000",
				"red": "#800000",
				"green": "#008000",
				"yellow": "#808000",
				"blue": "#000080",
				"purple": "#800080",
				"cyan": "#008080",
				"white": "#c0c0c0"
			}
			""";

		TerminalThemeImport import = Converter.Import(scheme);

		TerminalTheme theme = Succeeded(import, TerminalThemeFormat.WindowsTerminal);
		Assert.Equal("#000000", theme.Background);
		Assert.Equal("#c0c0c0", theme.Foreground);
		Assert.Equal("#c0c0c0", theme.Cursor);
		Assert.Equal("#000000", theme.CursorAccent);
		Assert.Equal("#c0c0c040", theme.SelectionBackground);
		Assert.Null(theme.SelectionForeground);
		// The brights come from the normal colors with the lightness raised a quarter of the way to white.
		Assert.Equal("#404040", theme.BrightBlack);
		Assert.Equal("#e00000", theme.BrightRed);
		Assert.Equal("#00e0e0", theme.BrightCyan);
		Assert.Equal("#d0d0d0", theme.BrightWhite);
		Assert.True(theme.IsDark);
	}

	[Fact]
	public void Import_LightBackground_IsNotMarkedDark()
	{
		TerminalThemeImport import = Converter.Import(TerminalThemeSamples.AlacrittyYamlGruvbox.Replace("'0x282828'", "'0xfbf1c7'", StringComparison.Ordinal));

		Assert.False(Succeeded(import, TerminalThemeFormat.Alacritty).IsDark);
	}

	[Theory]
	[InlineData("")]
	[InlineData("   \r\n\t ")]
	public void Import_NothingToRead_Fails(string text) =>
		Assert.Equal("There is nothing here to import.", Failed(Converter.Import(text)));

	[Fact]
	public void Import_TooMuchText_Fails()
	{
		string huge = new('x', (1024 * 1024) + 1);

		Assert.Equal("This is too big to be a color scheme (the limit is 1 MB of text).", Failed(Converter.Import(huge)));
	}

	[Fact]
	public void Import_UnknownText_SaysWhatItCanRead() =>
		Assert.StartsWith("This is not a color scheme Mokaterm can read.", Failed(Converter.Import("hello, this is not a theme")), StringComparison.Ordinal);

	[Fact]
	public void Import_JsonThatIsNotAScheme_Fails() =>
		Assert.Equal(
			"There are no colors here to read as a Windows Terminal color scheme.",
			Failed(Converter.Import("{ \"hello\": \"world\", \"count\": 3 }")));

	[Fact]
	public void Import_BrokenJson_Fails() =>
		Assert.Equal("This is not valid JSON.", Failed(Converter.Import("{ \"name\": \"Broken\", ")));

	[Fact]
	public void Import_ITerm2FileWithoutAnsiColors_NamesTheMissingKey()
	{
		string plist = TerminalThemeSamples.ITerm2Dracula.Replace("<key>Ansi 0 Color</key>", "<key>Ansi 99 Color</key>", StringComparison.Ordinal);

		Assert.Equal("This looks like an iTerm2 file, but it has no Ansi 0 Color.", Failed(Converter.Import(plist)));
	}

	[Fact]
	public void Import_PlistWithoutColors_Fails()
	{
		string plist = """
			<?xml version="1.0" encoding="UTF-8"?>
			<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
			<plist version="1.0">
			<dict>
				<key>Normal Font</key>
				<string>MesloLGS-NF-Regular 13</string>
			</dict>
			</plist>
			""";

		Assert.Equal("There are no colors here to read as an iTerm2 file.", Failed(Converter.Import(plist)));
	}

	[Fact]
	public void Import_PlistNestedDeepEnoughToOverflowTheStack_FailsInsteadOfCrashing()
	{
		// Reading an element's text walks its children recursively: 140,000 levels, well under the 1 MB limit, used to
		// end the process with a stack overflow that no catch can stop.
		const int depth = 140_000;
		string plist = "<plist><dict><key>"
			+ string.Concat(Enumerable.Repeat("<a>", depth)) + "x" + string.Concat(Enumerable.Repeat("</a>", depth))
			+ "</key><dict/></dict></plist>";

		Assert.Equal("This is not a readable property list.", Failed(Converter.Import(plist)));
	}

	[Fact]
	public void Import_FallsBackToTheFileExtension()
	{
		// Two resources are too few to recognize by content, so only the file name says what this is.
		string resources = "*.color0: #000000\n*.color1: #ff0000\n";

		Assert.StartsWith("This is not a color scheme", Failed(Converter.Import(resources)), StringComparison.Ordinal);
		Assert.Equal("This looks like an X resources file, but it has no *.color2.", Failed(Converter.Import(resources, "mine.Xresources")));
	}

	private static string Extension(TerminalThemeFormat format) => format switch
	{
		TerminalThemeFormat.ITerm2 => ".itermcolors",
		TerminalThemeFormat.Alacritty => ".toml",
		TerminalThemeFormat.Xresources => ".Xresources",
		_ => ".json",
	};

	private static TerminalTheme Succeeded(TerminalThemeImport import, TerminalThemeFormat format)
	{
		Assert.True(import.Succeeded, import.Error);
		Assert.Null(import.Error);
		Assert.Equal(format, import.Format);
		Assert.True(TerminalThemeRules.IsComplete(import.Theme), "The imported theme must pass the custom theme rules.");
		return import.Theme!;
	}

	private static string Failed(TerminalThemeImport import)
	{
		Assert.False(import.Succeeded);
		Assert.Null(import.Theme);
		return import.Error!;
	}

	private static CoreTestContext CreateContext() =>
		new(services => services.AddSingleton<IAppDataStore>(new InMemoryAppDataStore()));

	private static string[] Dracula =>
	[
		"#21222c", "#ff5555", "#50fa7b", "#f1fa8c", "#bd93f9", "#ff79c6", "#8be9fd", "#f8f8f2",
		"#6272a4", "#ff6e6e", "#69ff94", "#ffffa5", "#d6acff", "#ff92df", "#a4ffff", "#ffffff",
	];

	private static string[] Palette(TerminalTheme theme) =>
	[
		theme.Black, theme.Red, theme.Green, theme.Yellow, theme.Blue, theme.Magenta, theme.Cyan, theme.White,
		theme.BrightBlack, theme.BrightRed, theme.BrightGreen, theme.BrightYellow, theme.BrightBlue, theme.BrightMagenta, theme.BrightCyan, theme.BrightWhite,
	];

	/// <summary>A theme where every slot differs, so a lost or swapped color shows up.</summary>
	private static TerminalTheme Sample() => new()
	{
		Id = "sample",
		Name = "Sample Theme",
		Background = "#101214",
		Foreground = "#e4e6eb",
		Cursor = "#ff4081",
		CursorAccent = "#1b1d22",
		SelectionBackground = "#3366aa80",
		SelectionForeground = "#fafafa",
		Black = "#0a0c0e",
		Red = "#bf616a",
		Green = "#a3be8c",
		Yellow = "#ebcb8b",
		Blue = "#5e81ac",
		Magenta = "#b48ead",
		Cyan = "#88c0d0",
		White = "#d8dee9",
		BrightBlack = "#4c566a",
		BrightRed = "#d08770",
		BrightGreen = "#b5d99c",
		BrightYellow = "#f0d399",
		BrightBlue = "#81a1c1",
		BrightMagenta = "#c9a0c0",
		BrightCyan = "#8fbcbb",
		BrightWhite = "#f5f7fa",
	};
}
