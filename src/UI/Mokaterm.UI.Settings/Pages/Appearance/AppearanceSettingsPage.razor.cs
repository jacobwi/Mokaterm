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

	private static readonly IReadOnlyList<EnumOption<UiDensity>> DensityOptions =
	[
		new(UiDensity.Compact, "Compact"),
		new(UiDensity.Cozy, "Cozy"),
		new(UiDensity.Comfortable, "Comfortable"),
	];

	private static readonly IReadOnlyList<EnumOption<TooltipMotion>> TooltipMotionOptions =
	[
		new(TooltipMotion.Fade, "Fade"),
		new(TooltipMotion.Slide, "Slide"),
		new(TooltipMotion.Grow, "Grow"),
		new(TooltipMotion.Instant, "Instant"),
	];

	private static readonly IReadOnlyList<EnumOption<TooltipSize>> TooltipSizeOptions =
	[
		new(TooltipSize.Compact, "Compact"),
		new(TooltipSize.Standard, "Standard"),
		new(TooltipSize.Roomy, "Roomy"),
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

	private Task PickAsync(MokaAppearance picked) => SaveAsync(s => AppearanceChoice.Apply(s, picked));

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
