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

	/// <summary>Shortest <see cref="HostFiguresSeconds"/> the settings page offers and a saved file keeps.</summary>
	public const int MinHostFiguresSeconds = 2;

	/// <summary>Longest <see cref="HostFiguresSeconds"/> the settings page offers and a saved file keeps.</summary>
	public const int MaxHostFiguresSeconds = 60;

	public const int DefaultHostFiguresSeconds = 5;

	public static string SectionKey => "appearance";

	public ThemeMode Mode { get; init; } = ThemeMode.Dark;

	/// <summary><c>#rgb</c> or <c>#rrggbb</c> accent applied through <c>MokaTheme.WithAccent</c>.</summary>
	public string AccentColor { get; init; } = DefaultAccentColor;

	public UiDensity Density { get; init; } = UiDensity.Cozy;

	/// <summary>Multiplies every UI font size. 1.0 is the Moka default.</summary>
	public double FontScale { get; init; } = DefaultFontScale;

	public bool ShowStatusBar { get; init; } = true;

	/// <summary>
	/// Shows the CPU, memory and root disk use of the active session's machine in the status bar, for sessions that can
	/// read them. Reading them runs a short command on that machine every <see cref="HostFiguresSeconds"/>.
	/// </summary>
	public bool ShowHostFigures { get; init; } = true;

	/// <summary>How often the status bar's host figures are read while they show, in seconds.</summary>
	public int HostFiguresSeconds { get; init; } = DefaultHostFiguresSeconds;

	/// <summary>How the bar above a session arranges its buttons.</summary>
	public SessionToolbarLayout ToolbarLayout { get; init; } = SessionToolbarLayout.Grouped;

	/// <summary>
	/// The session toolbar's buttons the user turned off, by the shell's ids. They stay in the toolbar's overflow menu
	/// and in the command palette.
	/// </summary>
	public IReadOnlyList<string> HiddenToolbarButtons { get; init; } = [];

	/// <summary>
	/// Opens tools, session tools and the top bar's app tools alike, in windows of their own where the host has windows,
	/// which is the desktop app. A browser keeps them in dialogs whatever this says.
	/// </summary>
	public bool ToolsInOwnWindow { get; init; } = true;

	/// <summary>
	/// Brings back at start the tool windows that were open when Mokaterm closed. Desktop only, like the windows.
	/// </summary>
	public bool ReopenToolWindows { get; init; } = true;

	/// <summary>
	/// Opens each tool's window where that tool's window was last, at its size, and on top if it stayed on top. Off,
	/// every window opens at the tool's default size where the system puts it, and nothing about it is kept.
	/// </summary>
	public bool RememberToolWindowPlaces { get; init; } = true;

	/// <summary>
	/// Puts an app tool's one-line description after its title in its dialog or window header. Off, the header holds
	/// the icon and the title (and a session tool's session) and the description shows when the title is pointed at.
	/// </summary>
	public bool ShowToolDescriptions { get; init; }

	/// <summary>A copy with the accent color, the font scale and the host figures' interval inside the range the settings page offers.</summary>
	public AppearanceSettings Clamped() => this with
	{
		AccentColor = HexColor.Normalize(AccentColor, HexColorForms.Accent) ?? DefaultAccentColor,
		FontScale = SettingsRange.Clamp(FontScale, MinFontScale, MaxFontScale, DefaultFontScale),
		HostFiguresSeconds = Math.Clamp(HostFiguresSeconds, MinHostFiguresSeconds, MaxHostFiguresSeconds),
	};
}
