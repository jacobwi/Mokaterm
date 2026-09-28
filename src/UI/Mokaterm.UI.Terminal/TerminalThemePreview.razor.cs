using System.Text;
using Microsoft.AspNetCore.Components;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.UI.Terminal.Internal;

namespace Mokaterm.UI.Terminal;

/// <summary>
/// A static sample of a terminal theme for settings pages: a prompt, an <c>ls --color</c> listing and the 16 ANSI
/// colors. Plain HTML with no xterm.js instance, so a grid of them stays cheap.
/// </summary>
public partial class TerminalThemePreview : ComponentBase
{
	private static readonly (string Variable, Func<TerminalTheme, string?> Color)[] ThemeColors =
	[
		("--mt-fg", theme => theme.Foreground),
		("--mt-bg", theme => theme.Background),
		("--mt-cursor", theme => theme.Cursor),
		("--mt-cursor-accent", theme => theme.CursorAccent),
		("--mt-selection-fg", theme => theme.SelectionForeground),
		("--mt-black", theme => theme.Black),
		("--mt-red", theme => theme.Red),
		("--mt-green", theme => theme.Green),
		("--mt-yellow", theme => theme.Yellow),
		("--mt-blue", theme => theme.Blue),
		("--mt-magenta", theme => theme.Magenta),
		("--mt-cyan", theme => theme.Cyan),
		("--mt-white", theme => theme.White),
		("--mt-bright-black", theme => theme.BrightBlack),
		("--mt-bright-red", theme => theme.BrightRed),
		("--mt-bright-green", theme => theme.BrightGreen),
		("--mt-bright-yellow", theme => theme.BrightYellow),
		("--mt-bright-blue", theme => theme.BrightBlue),
		("--mt-bright-magenta", theme => theme.BrightMagenta),
		("--mt-bright-cyan", theme => theme.BrightCyan),
		("--mt-bright-white", theme => theme.BrightWhite),
	];

	// The 16 ANSI variables above, in palette order.
	private static readonly string[] SwatchStyles =
		[.. ThemeColors.Skip(5).Select(color => $"background-color:var({color.Variable})")];

	private TerminalTheme? _renderedTheme;
	private string? _renderedFontFamily;
	private double _renderedFontSize;
	private string? _renderedClass;
	private string _style = "";
	private bool _changed = true;

	[Parameter, EditorRequired]
	public TerminalTheme Theme { get; set; } = default!;

	[Parameter]
	public string? FontFamily { get; set; }

	[Parameter]
	public double FontSize { get; set; } = 13;

	[Parameter]
	public string? Class { get; set; }

	private string RootClass => string.IsNullOrWhiteSpace(Class) ? "mt-theme-preview" : "mt-theme-preview " + Class;

	protected override void OnParametersSet()
	{
		// TerminalTheme is a record, so a settings page that rebuilds its list still skips unchanged previews.
		_changed = Theme != _renderedTheme
			|| !string.Equals(FontFamily, _renderedFontFamily, StringComparison.Ordinal)
			|| !FontSize.Equals(_renderedFontSize)
			|| !string.Equals(Class, _renderedClass, StringComparison.Ordinal);
		if (!_changed)
		{
			return;
		}

		_renderedTheme = Theme;
		_renderedFontFamily = FontFamily;
		_renderedFontSize = FontSize;
		_renderedClass = Class;
		_style = BuildStyle(Theme, FontFamily, FontSize);
	}

	protected override bool ShouldRender() => _changed;

	private static string BuildStyle(TerminalTheme theme, string? fontFamily, double fontSize)
	{
		StringBuilder style = new();
		style.Append("font-family:").Append(CssValues.FontFamily(fontFamily))
			.Append(";font-size:").Append(CssValues.Pixels(Math.Clamp(fontSize, 6, 72))).Append(';');

		foreach ((string variable, Func<TerminalTheme, string?> color) in ThemeColors)
		{
			AppendVariable(style, variable, CssValues.Color(color(theme)));
		}

		AppendVariable(style, "--mt-selection", SelectionColor(CssValues.Color(theme.SelectionBackground)));
		return style.ToString();
	}

	private static void AppendVariable(StringBuilder style, string variable, string? value)
	{
		if (value is not null)
		{
			style.Append(variable).Append(':').Append(value).Append(';');
		}
	}

	// xterm.js draws an opaque selection color at 30% opacity; the sample does the same so it matches the terminal.
	private static string? SelectionColor(string? color)
	{
		if (color is null)
		{
			return null;
		}

		return color.Length switch
		{
			4 => string.Concat("#", new string(color[1], 2), new string(color[2], 2), new string(color[3], 2), "4d"),
			7 => color + "4d",
			_ => color,
		};
	}
}
