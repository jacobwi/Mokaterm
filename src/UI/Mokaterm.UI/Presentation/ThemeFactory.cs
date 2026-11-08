using Moka.Red.Core.Theming;
using Mokaterm.Abstractions.Settings;

namespace Mokaterm.UI.Presentation;

/// <summary>Builds the Moka theme for <see cref="AppearanceSettings"/>.</summary>
internal static class ThemeFactory
{
	public static MokaTheme Create(AppearanceSettings settings)
	{
		// Clamped first: settings.json can be edited by hand, and a zero or absurd scale would make the whole UI
		// unusable. The section owns that range, so the slider and the theme cannot disagree about what is applied.
		AppearanceSettings usable = settings.Clamped();
		MokaTheme theme = usable.Mode == ThemeMode.Dark ? MokaTheme.Red : MokaTheme.RedLight;

		// Red tunes its own accent shades for contrast. WithAccent reads a color as a hue and finds new shades, so the
		// default red would come out as a slightly different red than the one the theme was drawn with.
		if (!string.Equals(usable.AccentColor, AppearanceSettings.DefaultAccentColor, StringComparison.OrdinalIgnoreCase))
		{
			theme = theme.WithAccent(usable.AccentColor);
		}

		theme = usable.Density switch
		{
			UiDensity.Compact => theme.WithDensity(MokaDensity.Compact),
			UiDensity.Comfortable => theme.WithDensity(1.15),
			_ => theme,
		};

		return theme.WithStatusBar(Look(usable.StatusBarStyle)).WithFontScale(usable.FontScale);
	}

	private static MokaStatusBarLook Look(StatusBarStyle style) => style switch
	{
		StatusBarStyle.Accent => MokaStatusBarLook.Accent,
		StatusBarStyle.Hairline => MokaStatusBarLook.Hairline,
		StatusBarStyle.Floating => MokaStatusBarLook.Floating,
		StatusBarStyle.Chips => MokaStatusBarLook.Chips,
		StatusBarStyle.Segments => MokaStatusBarLook.Segments,
		StatusBarStyle.Terminal => MokaStatusBarLook.Terminal,
		_ => MokaStatusBarLook.Standard,
	};
}
