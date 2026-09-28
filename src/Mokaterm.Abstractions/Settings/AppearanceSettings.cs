using Mokaterm.Abstractions.Presentation;

namespace Mokaterm.Abstractions.Settings;

public enum ThemeMode
{
	Dark,
	Light,
}

public enum UiDensity
{
	Compact,
	Cozy,
	Comfortable,
}

/// <summary>Application theme. Terminal colors live in <see cref="TerminalSettings"/>.</summary>
public sealed record AppearanceSettings : ISettingsSection
{
	/// <summary>Smallest <see cref="FontScale"/> the settings page offers and a saved file keeps.</summary>
	public const double MinFontScale = 0.75;

	/// <summary>Largest <see cref="FontScale"/> the settings page offers and a saved file keeps.</summary>
	public const double MaxFontScale = 1.5;

	public const double DefaultFontScale = 1.0;

	public const string DefaultAccentColor = "#ef5350";

	public static string SectionKey => "appearance";

	public ThemeMode Mode { get; init; } = ThemeMode.Dark;

	/// <summary><c>#rgb</c> or <c>#rrggbb</c> accent applied through <c>MokaTheme.WithAccent</c>.</summary>
	public string AccentColor { get; init; } = DefaultAccentColor;

	public UiDensity Density { get; init; } = UiDensity.Cozy;

	/// <summary>Multiplies every UI font size. 1.0 is the Moka default.</summary>
	public double FontScale { get; init; } = DefaultFontScale;

	public bool ShowStatusBar { get; init; } = true;

	/// <summary>A copy with the accent color and the font scale inside the range the settings page offers.</summary>
	public AppearanceSettings Clamped() => this with
	{
		AccentColor = HexColor.Normalize(AccentColor, HexColorForms.Accent) ?? DefaultAccentColor,
		FontScale = SettingsRange.Clamp(FontScale, MinFontScale, MaxFontScale, DefaultFontScale),
	};
}
