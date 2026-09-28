using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.UI.Terminal.Interop;

namespace Mokaterm.UI.Tests;

public sealed class TerminalJsOptionsTests
{
	private static readonly TerminalTheme Theme = new()
	{
		Id = "test",
		Name = "Test",
		Foreground = "#e8e8ec",
		Background = "#060608",
		Cursor = "#ef5350",
		CursorAccent = "#060608",
		SelectionBackground = "#ef535040",
		Black = "#101015",
		Red = "#ef5350",
		Green = "#00e676",
		Yellow = "#ffd54f",
		Blue = "#42a5f5",
		Magenta = "#ce93d8",
		Cyan = "#26c6da",
		White = "#a0a0aa",
		BrightBlack = "#40404a",
		BrightRed = "#ff6b68",
		BrightGreen = "#69f0ae",
		BrightYellow = "#ffe082",
		BrightBlue = "#90caf9",
		BrightMagenta = "#e1bee7",
		BrightCyan = "#80deea",
		BrightWhite = "#e8e8ec",
	};

	// xterm.js's DOM renderer writes the font family into a style element, so a closing brace would add page-wide rules.
	[Fact]
	public void From_FontFamilyThatWouldCloseTheStyleRule_IsCleaned()
	{
		TerminalJsOptions options = TerminalJsOptions.From(new TerminalSettings { FontFamily = "Consolas } body { display: none } .x {" }, Theme);

		Assert.DoesNotContain("}", options.FontFamily, StringComparison.Ordinal);
		Assert.DoesNotContain("{", options.FontFamily, StringComparison.Ordinal);
		Assert.StartsWith("Consolas", options.FontFamily, StringComparison.Ordinal);
	}

	[Fact]
	public void From_OrdinaryFontList_IsKept()
	{
		const string fonts = "'JetBrains Mono', \"Cascadia Mono\", Consolas, monospace";

		Assert.Equal(fonts, TerminalJsOptions.From(new TerminalSettings { FontFamily = fonts }, Theme).FontFamily);
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("{};")]
	public void From_BlankOrUnusableFontFamily_FallsBackToMonospace(string fonts) =>
		Assert.Equal("monospace", TerminalJsOptions.From(new TerminalSettings { FontFamily = fonts }, Theme).FontFamily);
}
