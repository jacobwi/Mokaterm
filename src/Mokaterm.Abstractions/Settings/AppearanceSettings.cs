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

	/// <summary>The most entries <see cref="HiddenToolbarButtons"/> keeps. The toolbar has far fewer buttons than this.</summary>
	public const int MaxHiddenToolbarButtons = 64;

	/// <summary>The longest button id <see cref="HiddenToolbarButtons"/> keeps.</summary>
	public const int MaxToolbarButtonIdLength = 64;

	public static string SectionKey => "appearance";

	public ThemeMode Mode { get; init; } = ThemeMode.Dark;

	/// <summary><c>#rgb</c> or <c>#rrggbb</c> accent applied through <c>MokaTheme.WithAccent</c>.</summary>
	public string AccentColor { get; init; } = DefaultAccentColor;

	public UiDensity Density { get; init; } = UiDensity.Cozy;

	/// <summary>Multiplies every UI font size. 1.0 is the Moka default.</summary>
	public double FontScale { get; init; } = DefaultFontScale;

	public bool ShowStatusBar { get; init; } = true;

	/// <summary>How the bar above a session arranges its buttons.</summary>
	public SessionToolbarLayout ToolbarLayout { get; init; } = SessionToolbarLayout.Grouped;

	/// <summary>
	/// The session toolbar's buttons the user turned off, by the shell's ids. They stay in the toolbar's overflow menu
	/// and in the command palette.
	/// </summary>
	public IReadOnlyList<string> HiddenToolbarButtons { get; init; } = [];

	/// <summary>
	/// Opens session tools in windows of their own where the host has windows, which is the desktop app. A browser
	/// keeps them in dialogs whatever this says.
	/// </summary>
	public bool ToolsInOwnWindow { get; init; } = true;

	/// <summary>A copy with the accent color and the font scale inside the range the settings page offers.</summary>
	public AppearanceSettings Clamped() => this with
	{
		AccentColor = HexColor.Normalize(AccentColor, HexColorForms.Accent) ?? DefaultAccentColor,
		FontScale = SettingsRange.Clamp(FontScale, MinFontScale, MaxFontScale, DefaultFontScale),
	};
}
