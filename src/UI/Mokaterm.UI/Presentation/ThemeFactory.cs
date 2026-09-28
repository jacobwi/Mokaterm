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
		MokaTheme theme = usable.Mode == ThemeMode.Dark ? MokaTheme.Dark : MokaTheme.Light;
		theme = theme.WithAccent(usable.AccentColor);

		double density = usable.Density switch
		{
			UiDensity.Compact => 0.75,
			UiDensity.Comfortable => 1.15,
			_ => 1.0,
		};

		return theme.WithDensity(density).WithFontScale(usable.FontScale);
	}
}
