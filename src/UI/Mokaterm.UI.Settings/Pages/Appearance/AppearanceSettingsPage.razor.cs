using Microsoft.AspNetCore.Components;
using Moka.Red.Core.Theming;
using Moka.Red.Icons;
using Moka.Red.ThemeGen.Appearance;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Presentation;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Components;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.Icons;

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

	private static readonly IReadOnlyList<EnumOption<UiDensity>> DensityOptions =
	[
		new(UiDensity.Compact, "Compact"),
		new(UiDensity.Cozy, "Cozy"),
		new(UiDensity.Comfortable, "Comfortable"),
	];

	private static readonly IReadOnlyList<EnumOption<StatusBarStyle>> StatusBarStyleOptions =
	[
		new(StatusBarStyle.Standard, "Standard"),
		new(StatusBarStyle.Accent, "Accent"),
		new(StatusBarStyle.Hairline, "Hairline"),
		new(StatusBarStyle.Floating, "Floating"),
		new(StatusBarStyle.Chips, "Chips"),
		new(StatusBarStyle.Segments, "Segments"),
		new(StatusBarStyle.Terminal, "Terminal"),
	];

	private static readonly IReadOnlyList<EnumOption<SessionToolbarLayout>> ToolbarLayoutOptions =
	[
		new(SessionToolbarLayout.Grouped, "Grouped", MokatermIcons.Tools),
		new(SessionToolbarLayout.Flat, "Flat", MokaIcons.Navigation.MoreHorizontal),
		new(SessionToolbarLayout.Compact, "Compact", MokaIcons.Navigation.Menu),
	];

	private string _previewHost = "";
	private string? _previewAccentColor;
	private IReadOnlyList<MokaAccent> _previewAccents = [];

	[Inject]
	private IUiContributions Contributions { get; set; } = default!;

	[Inject]
	private IAppEnvironment AppEnvironment { get; set; } = default!;

	/// <summary>The shell's buttons, then one per tool the loaded modules add.</summary>
	private IReadOnlyList<SessionToolbarButton> ToolbarButtons => SessionToolbarButtons.All(Contributions);

	// Kept from one render to the next: the panel rebuilds its pictures' themes whenever it is handed another list.
	private IReadOnlyList<MokaAccent> PreviewAccents(AppearanceSettings settings)
	{
		if (!string.Equals(_previewAccentColor, settings.AccentColor, StringComparison.Ordinal))
		{
			_previewAccentColor = settings.AccentColor;
			_previewAccents = AppearanceChoice.AccentsFor(settings);
		}

		return _previewAccents;
	}

	private Task SetModeAsync(MokaAppearance appearance) =>
		UpdateAsync(s => s with { Mode = AppearanceChoice.ModeFrom(appearance.Mode, s.Mode) });

	private static string? SelectedPreset(string accent) =>
		Array.Find(AccentPresets, preset => string.Equals(preset, accent, StringComparison.OrdinalIgnoreCase));

	private Task SelectPresetAsync(string? color) =>
		color is null ? Task.CompletedTask : SaveAsync(s => s with { AccentColor = color });

	private Task SetFontScaleAsync(double value) =>
		// Range inputs report binary fractions such as 1.1500000000000001; store the step value instead. The range
		// itself is the section's, so this only rounds.
		SaveAsync(s => s with { FontScale = Math.Round(value, 2) });

	private Task SaveAsync(Func<AppearanceSettings, AppearanceSettings> change) => UpdateAsync(s => change(s).Clamped());

	private Task SetButtonShownAsync(string id, bool shown) => UpdateAsync(s => s with
	{
		HiddenToolbarButtons = shown
			? [.. s.HiddenToolbarButtons.Where(hidden => hidden != id)]
			: [.. s.HiddenToolbarButtons.Where(hidden => hidden != id), id],
	});
}
