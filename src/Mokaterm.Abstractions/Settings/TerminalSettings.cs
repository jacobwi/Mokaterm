using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.Abstractions.Settings;

public sealed record TerminalSettings : ISettingsSection
{
	/// <summary>Smallest <see cref="ReplayBufferKilobytes"/> the settings page offers and a saved file keeps.</summary>
	public const int MinReplayBufferKilobytes = 16;

	/// <summary>Largest <see cref="ReplayBufferKilobytes"/> the settings page offers and a saved file keeps.</summary>
	public const int MaxReplayBufferKilobytes = 16_384;

	/// <summary>
	/// The <see cref="FontSize"/> range, which a connection's own override in <see cref="TerminalProfileOverrides"/>
	/// shares: a size the editor refuses must not reach a terminal through a saved login either.
	/// </summary>
	public const double MinFontSize = 6;

	/// <inheritdoc cref="MinFontSize"/>
	public const double MaxFontSize = 48;

	public const double DefaultFontSize = 13;

	public const double MinLineHeight = 1;

	public const double MaxLineHeight = 2;

	public const double DefaultLineHeight = 1.15;

	public const double MinLetterSpacing = 0;

	public const double MaxLetterSpacing = 10;

	public const int MinScrollback = 0;

	public const int MaxScrollback = 100_000;

	/// <summary>1 leaves the theme alone, and 21 is black on white, the highest ratio there is.</summary>
	public const double MinContrastRatio = 1;

	/// <inheritdoc cref="MinContrastRatio"/>
	public const double MaxContrastRatio = 21;

	public static string SectionKey => "terminal";

	public string ThemeId { get; init; } = "moka-dark";

	/// <summary>A CSS font list; <see cref="TerminalFontFamily"/> says what it may hold.</summary>
	public string FontFamily { get; init; } = "'JetBrains Mono', 'Cascadia Mono', Consolas, 'Courier New', monospace";

	public double FontSize { get; init; } = DefaultFontSize;

	public double LineHeight { get; init; } = DefaultLineHeight;

	public double LetterSpacing { get; init; }

	public TerminalCursorStyle CursorStyle { get; init; } = TerminalCursorStyle.Block;

	public bool CursorBlink { get; init; } = true;

	/// <summary>Lines kept in the view's scrollback.</summary>
	public int Scrollback { get; init; } = 10_000;

	public bool CopyOnSelect { get; init; }

	public TerminalRightClickAction RightClickAction { get; init; } = TerminalRightClickAction.ContextMenu;

	public TerminalBellStyle Bell { get; init; } = TerminalBellStyle.Visual;

	/// <summary>Ask before pasting text that contains line breaks, which would run commands.</summary>
	public bool ConfirmMultiLinePaste { get; init; } = true;

	/// <summary>
	/// Let programs on the remote side copy to the local clipboard with OSC 52, as tmux and Neovim do. Off by default: a
	/// hostile server could plant a command to be pasted elsewhere. Reading the clipboard is never allowed.
	/// </summary>
	public bool AllowRemoteClipboardWrite { get; init; }

	/// <summary>Use the WebGL renderer when the WebView supports it; falls back to the DOM renderer.</summary>
	public bool UseWebGl { get; init; } = true;

	/// <summary>Value sent as TERM when the protocol requests a pseudo-terminal.</summary>
	public string TerminalType { get; init; } = "xterm-256color";

	/// <summary>Output kept per session for views that attach later, in kilobytes.</summary>
	public int ReplayBufferKilobytes { get; init; } = 256;

	public IReadOnlyList<TerminalTheme> CustomThemes { get; init; } = [];

	/// <summary>
	/// Lowest contrast between text and its background, as a ratio. 1 leaves the theme alone; 4.5 is the WCAG AA level
	/// for body text. Colors that fall below it are lightened or darkened until they reach it.
	/// </summary>
	public double MinimumContrastRatio { get; init; } = MinContrastRatio;

	/// <summary>Draw bold text in the bright color of its palette entry, as most terminals do.</summary>
	public bool DrawBoldTextInBrightColors { get; init; } = true;

	/// <summary>
	/// Color the user, the machine and the folder of a <c>user@host:path$</c> prompt, with root in red. By default only a
	/// prompt the server sends in one plain color is painted; one the server colors itself stays as it is.
	/// </summary>
	public TerminalPromptColors PromptColors { get; init; } = TerminalPromptColors.WhenPlain;

	/// <summary>Theme for sessions on hosts marked as production, so the wrong window is recognizable. Null uses <see cref="ThemeId"/>.</summary>
	public string? ProductionThemeId { get; init; }

	/// <inheritdoc cref="ProductionThemeId"/>
	public string? StagingThemeId { get; init; }

	/// <inheritdoc cref="ProductionThemeId"/>
	public string? DevelopmentThemeId { get; init; }

	/// <summary>The theme set for <paramref name="environment"/>, or null when those hosts use the normal theme.</summary>
	public string? ThemeIdFor(HostEnvironment environment) => environment switch
	{
		HostEnvironment.Production => NullIfBlank(ProductionThemeId),
		HostEnvironment.Staging => NullIfBlank(StagingThemeId),
		HostEnvironment.Development => NullIfBlank(DevelopmentThemeId),
		_ => null,
	};

	/// <summary>These settings with the theme for <paramref name="environment"/>, when one is set.</summary>
	public TerminalSettings ForEnvironment(HostEnvironment environment) =>
		ThemeIdFor(environment) is { } themeId ? this with { ThemeId = themeId } : this;

	/// <summary>
	/// These settings with a connection's overrides applied on top. An override goes through the same range as the
	/// setting it replaces: the connection editor refuses what this would clamp, and a login saved by an older build or
	/// edited by hand cannot open a terminal at a size the settings page would not offer.
	/// </summary>
	public TerminalSettings WithOverrides(TerminalProfileOverrides? overrides) =>
		overrides is null || overrides.IsEmpty
			? this
			: this with
			{
				ThemeId = overrides.ThemeId ?? ThemeId,
				FontFamily = overrides.FontFamily ?? FontFamily,
				FontSize = overrides.FontSize is { } size ? SettingsRange.Clamp(size, MinFontSize, MaxFontSize, FontSize) : FontSize,
			};

	/// <summary>A copy with every number inside the range the settings page offers.</summary>
	public TerminalSettings Clamped() => this with
	{
		FontSize = SettingsRange.Clamp(FontSize, MinFontSize, MaxFontSize, DefaultFontSize),
		LineHeight = SettingsRange.Clamp(LineHeight, MinLineHeight, MaxLineHeight, DefaultLineHeight),
		LetterSpacing = SettingsRange.Clamp(LetterSpacing, MinLetterSpacing, MaxLetterSpacing, MinLetterSpacing),
		Scrollback = Math.Clamp(Scrollback, MinScrollback, MaxScrollback),
		ReplayBufferKilobytes = Math.Clamp(ReplayBufferKilobytes, MinReplayBufferKilobytes, MaxReplayBufferKilobytes),
		MinimumContrastRatio = SettingsRange.Clamp(MinimumContrastRatio, MinContrastRatio, MaxContrastRatio, MinContrastRatio),
	};

	private static string? NullIfBlank(string? themeId) => string.IsNullOrWhiteSpace(themeId) ? null : themeId;
}
