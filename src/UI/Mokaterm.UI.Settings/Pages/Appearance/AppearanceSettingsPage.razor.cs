using Moka.Red.Icons;
using Mokaterm.Abstractions.Presentation;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Components;

namespace Mokaterm.UI.Settings.Pages.Appearance;

/// <summary>Settings page for <see cref="AppearanceSettings"/> with a live preview built from Moka.Red components.</summary>
public sealed partial class AppearanceSettingsPage : SettingsSectionBase<AppearanceSettings>
{
	private const double FontScaleStep = 0.05;

	private static readonly string AccentHelp =
		$"Buttons, focus rings and selections. Moka red ({AppearanceSettings.DefaultAccentColor}) is the default. "
		+ HexColor.Requirement(HexColorForms.Accent);

	private static readonly string[] AccentPresets =
		[AppearanceSettings.DefaultAccentColor, "#42a5f5", "#00e676", "#ffab40", "#ce93d8", "#ffd54f"];

	private static readonly IReadOnlyList<EnumOption<ThemeMode>> ModeOptions =
	[
		new(ThemeMode.Dark, "Dark", MokaIcons.Action.Moon),
		new(ThemeMode.Light, "Light", MokaIcons.Action.Sun),
	];

	private static readonly IReadOnlyList<EnumOption<UiDensity>> DensityOptions =
	[
		new(UiDensity.Compact, "Compact"),
		new(UiDensity.Cozy, "Cozy"),
		new(UiDensity.Comfortable, "Comfortable"),
	];

	private string _previewHost = "";

	private static string? SelectedPreset(string accent) =>
		Array.Find(AccentPresets, preset => string.Equals(preset, accent, StringComparison.OrdinalIgnoreCase));

	private Task SelectPresetAsync(string? color) =>
		color is null ? Task.CompletedTask : SaveAsync(s => s with { AccentColor = color });

	private Task SetFontScaleAsync(double value) =>
		// Range inputs report binary fractions such as 1.1500000000000001; store the step value instead. The range
		// itself is the section's, so this only rounds.
		SaveAsync(s => s with { FontScale = Math.Round(value, 2) });

	private Task SaveAsync(Func<AppearanceSettings, AppearanceSettings> change) => UpdateAsync(s => change(s).Clamped());
}
