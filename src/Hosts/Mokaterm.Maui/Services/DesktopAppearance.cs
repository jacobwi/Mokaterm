using Moka.Red.Core.Theming;
using Mokaterm.Abstractions.Settings;

namespace Mokaterm.Maui.Services;

// Light or dark for everything the app draws before its page can, and outside it: the windows' own background,
// WebView2's background, the colour scheme the boot and closing screens follow (moka-boot.css reads
// prefers-color-scheme) and the title bar's buttons. It starts from settings.json, read before any window exists, and
// follows the settings once the page has loaded or changed them.
public sealed class DesktopAppearance
{
	// The settings service's plain document, which the store keeps as settings.json in the data folder.
	private const string SettingsFile = "settings.json";

	private readonly ISettingsService _settings;
	private volatile bool _light;

	public DesktopAppearance(ISettingsService settings)
	{
		_settings = settings;
		_light = SettingsFileMode.ReadIsLight(Path.Combine(DesktopPaths.DataDirectory, SettingsFile));
		StartupTrace.Mark("Theme mode read");
		settings.Changed += OnSettingsChanged;
	}

	/// <summary>Raised when the mode changes. May fire on any thread.</summary>
	public event Action? Changed;

	public bool IsLight => _light;

	/// <summary>The page's own background in this mode, as #rrggbb.</summary>
	public string Background => (IsLight ? MokaTheme.RedLight : MokaTheme.Red).Palette.Background;

	private void OnSettingsChanged(string sectionKey)
	{
		if (sectionKey != AppearanceSettings.SectionKey)
		{
			return;
		}

		bool light = _settings.Get<AppearanceSettings>().Mode == ThemeMode.Light;
		if (light != _light)
		{
			_light = light;
			Changed?.Invoke();
		}
	}
}
