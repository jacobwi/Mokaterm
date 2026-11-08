using Moka.Red.Core.Theming;
using Moka.Red.ThemeGen.Appearance;
using Mokaterm.Abstractions.Settings;

namespace Mokaterm.UI.Settings.Pages.Appearance;

/// <summary>
/// What the Appearance page hands Moka's appearance panel, read from <see cref="AppearanceSettings"/>, and the one choice
/// that comes back. The panel only picks the mode here, so its value says nothing else that could reach the stored record.
/// </summary>
internal static class AppearanceChoice
{
	/// <summary>Only the design <c>ThemeFactory</c> builds, so the pictures show what the app will look like.</summary>
	public static IReadOnlyList<MokaAppearancePalette> Palettes { get; } = [MokaAppearancePalette.Red];

	public static MokaAppearanceText Words { get; } = new() { ColorMode = "Theme mode" };

	/// <summary>
	/// The accent the pictures are tinted with. The default stays no accent at all, as it does in <c>ThemeFactory</c>:
	/// Red tunes its own shades, and running the default through an accent would come out a slightly different red.
	/// </summary>
	public static IReadOnlyList<MokaAccent> AccentsFor(AppearanceSettings settings)
	{
		ArgumentNullException.ThrowIfNull(settings);
		string accent = settings.Clamped().AccentColor;
		return string.Equals(accent, AppearanceSettings.DefaultAccentColor, StringComparison.OrdinalIgnoreCase)
			? []
			: [MokaAccent.FromColor(accent)];
	}

	public static MokaAppearance ValueFor(AppearanceSettings settings, IReadOnlyList<MokaAccent> accents)
	{
		ArgumentNullException.ThrowIfNull(settings);
		ArgumentNullException.ThrowIfNull(accents);
		return new MokaAppearance
		{
			Mode = settings.Mode == ThemeMode.Light ? MokaColorMode.Light : MokaColorMode.Dark,
			Accent = accents.Count > 0 ? accents[0].Id : null,
		};
	}

	/// <summary>The stored mode for a pick. A pick the app has no mode for changes nothing.</summary>
	public static ThemeMode ModeFrom(MokaColorMode picked, ThemeMode current) => picked switch
	{
		MokaColorMode.Light => ThemeMode.Light,
		MokaColorMode.Dark => ThemeMode.Dark,
		_ => current,
	};
}
