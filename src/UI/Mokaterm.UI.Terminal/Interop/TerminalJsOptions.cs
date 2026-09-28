using System.Text.Json;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.UI.Terminal.Internal;

namespace Mokaterm.UI.Terminal.Interop;

/// <summary>Everything terminal.js needs from the settings, in the shape and names the module reads.</summary>
internal sealed record TerminalJsOptions
{
	public required string FontFamily { get; init; }

	public required double FontSize { get; init; }

	public required double LineHeight { get; init; }

	public required double LetterSpacing { get; init; }

	public required string CursorStyle { get; init; }

	public required bool CursorBlink { get; init; }

	public required int Scrollback { get; init; }

	public required double MinimumContrastRatio { get; init; }

	public required bool DrawBoldTextInBrightColors { get; init; }

	public required TerminalTheme Theme { get; init; }

	public required bool UseWebGl { get; init; }

	public required string Bell { get; init; }

	public required bool CopyOnSelect { get; init; }

	public required string RightClickAction { get; init; }

	public required bool ConfirmMultiLinePaste { get; init; }

	public required bool AllowClipboardWrite { get; init; }

	/// <summary>"off", "plain" or "always".</summary>
	public required string PromptColors { get; init; }

	/// <summary>The machine's color for a prompt the server leaves plain: a CSS color, or a <c>var(...)</c> the page resolves.</summary>
	public string? PromptHostColor { get; init; }

	// xterm.js throws on a line height below 1 or negative scrollback, and a zero font size breaks rendering,
	// so values a hand-edited settings file could hold are clamped here. Its DOM renderer copies the font family into a
	// style element as it is, where a '}' would start rules of its own for the whole page.
	public static TerminalJsOptions From(TerminalSettings settings, TerminalTheme theme, string? promptHostColor = null) => new()
	{
		FontFamily = CssValues.FontFamily(settings.FontFamily),
		FontSize = Math.Clamp(settings.FontSize, 6, 72),
		LineHeight = Math.Clamp(settings.LineHeight, 1, 3),
		LetterSpacing = Math.Clamp(settings.LetterSpacing, -4, 16),
		CursorStyle = settings.CursorStyle switch
		{
			TerminalCursorStyle.Underline => "underline",
			TerminalCursorStyle.Bar => "bar",
			_ => "block",
		},
		CursorBlink = settings.CursorBlink,
		Scrollback = Math.Clamp(settings.Scrollback, 0, 1_000_000),
		MinimumContrastRatio = Math.Clamp(settings.MinimumContrastRatio, 1, 21),
		DrawBoldTextInBrightColors = settings.DrawBoldTextInBrightColors,
		Theme = theme,
		UseWebGl = settings.UseWebGl,
		Bell = settings.Bell switch
		{
			TerminalBellStyle.None => "none",
			TerminalBellStyle.Sound => "sound",
			_ => "visual",
		},
		CopyOnSelect = settings.CopyOnSelect,
		RightClickAction = settings.RightClickAction == TerminalRightClickAction.Paste ? "paste" : "menu",
		ConfirmMultiLinePaste = settings.ConfirmMultiLinePaste,
		AllowClipboardWrite = settings.AllowRemoteClipboardWrite,
		PromptColors = settings.PromptColors switch
		{
			TerminalPromptColors.Off => "off",
			TerminalPromptColors.Always => "always",
			_ => "plain",
		},
		PromptHostColor = string.IsNullOrWhiteSpace(promptHostColor) ? null : promptHostColor.Trim(),
	};

	/// <summary>The options that differ from <paramref name="previous"/>, keyed by their JavaScript names.</summary>
	public Dictionary<string, object?> ChangesSince(TerminalJsOptions previous)
	{
		Dictionary<string, object?> changes = new(StringComparer.Ordinal);
		AddIfChanged(changes, nameof(FontFamily), FontFamily, previous.FontFamily);
		AddIfChanged(changes, nameof(FontSize), FontSize, previous.FontSize);
		AddIfChanged(changes, nameof(LineHeight), LineHeight, previous.LineHeight);
		AddIfChanged(changes, nameof(LetterSpacing), LetterSpacing, previous.LetterSpacing);
		AddIfChanged(changes, nameof(CursorStyle), CursorStyle, previous.CursorStyle);
		AddIfChanged(changes, nameof(CursorBlink), CursorBlink, previous.CursorBlink);
		AddIfChanged(changes, nameof(Scrollback), Scrollback, previous.Scrollback);
		AddIfChanged(changes, nameof(MinimumContrastRatio), MinimumContrastRatio, previous.MinimumContrastRatio);
		AddIfChanged(changes, nameof(DrawBoldTextInBrightColors), DrawBoldTextInBrightColors, previous.DrawBoldTextInBrightColors);
		AddIfChanged(changes, nameof(Theme), Theme, previous.Theme);
		AddIfChanged(changes, nameof(UseWebGl), UseWebGl, previous.UseWebGl);
		AddIfChanged(changes, nameof(Bell), Bell, previous.Bell);
		AddIfChanged(changes, nameof(CopyOnSelect), CopyOnSelect, previous.CopyOnSelect);
		AddIfChanged(changes, nameof(RightClickAction), RightClickAction, previous.RightClickAction);
		AddIfChanged(changes, nameof(ConfirmMultiLinePaste), ConfirmMultiLinePaste, previous.ConfirmMultiLinePaste);
		AddIfChanged(changes, nameof(AllowClipboardWrite), AllowClipboardWrite, previous.AllowClipboardWrite);
		AddIfChanged(changes, nameof(PromptColors), PromptColors, previous.PromptColors);
		AddIfChanged(changes, nameof(PromptHostColor), PromptHostColor, previous.PromptHostColor);
		return changes;
	}

	// Blazor serializes interop arguments with camelCase names; dictionary keys have to follow the same rule.
	private static void AddIfChanged<T>(Dictionary<string, object?> changes, string name, T current, T previous)
	{
		if (!EqualityComparer<T>.Default.Equals(current, previous))
		{
			changes[JsonNamingPolicy.CamelCase.ConvertName(name)] = current;
		}
	}
}
