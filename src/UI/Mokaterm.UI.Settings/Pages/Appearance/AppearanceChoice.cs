using Moka.Red.Core.Theming;
using Moka.Red.ThemeGen.Appearance;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Presentation;

namespace Mokaterm.UI.Settings.Pages.Appearance;

// Translates between AppearanceSettings and the MokaAppearance the page's panels take and give back.
internal static class AppearanceChoice
{
	private const string CustomAccentId = "custom";

	private static readonly (string Id, string Name, string Color)[] AccentPresets =
	[
		("red", "Moka red", AppearanceSettings.DefaultAccentColor),
		("blue", "Blue", "#42a5f5"),
		("green", "Green", "#00e676"),
		("orange", "Orange", "#ffab40"),
		("purple", "Purple", "#ce93d8"),
		("yellow", "Yellow", "#ffd54f"),
	];

	private static readonly IReadOnlyList<MokaAccent> PresetAccents =
		[.. AccentPresets.Select(preset => MokaAccent.FromColor(preset.Color) with { Id = preset.Id, Name = preset.Name })];

	// Only the design ThemeFactory builds, so the pictures show what the app will look like.
	public static IReadOnlyList<MokaAppearancePalette> Palettes { get; } = [MokaAppearancePalette.Red];

	public static MokaAppearanceText Words { get; } = new()
	{
		ColorMode = "Theme mode",
		Accent = "Accent color",
		StatusBar = "Status bar style",
	};

	// The app stores light or dark, so following the device has nothing to save as.
	public static MokaAppearanceOptionState StateOf(MokaAppearanceOption option) =>
		option.Value is MokaColorMode.System ? MokaAppearanceOptionState.Hidden : MokaAppearanceOptionState.Available;

	public static IReadOnlyList<MokaAccent> AccentsFor(AppearanceSettings settings)
	{
		ArgumentNullException.ThrowIfNull(settings);
		string accent = settings.Clamped().AccentColor;
		return AccentIdFor(accent) == CustomAccentId
			? [.. PresetAccents, MokaAccent.FromColor(accent) with { Id = CustomAccentId, Name = accent }]
			: PresetAccents;
	}

	public static MokaAppearance ValueFor(AppearanceSettings settings)
	{
		ArgumentNullException.ThrowIfNull(settings);
		AppearanceSettings usable = settings.Clamped();
		return new MokaAppearance
		{
			Mode = usable.Mode == ThemeMode.Light ? MokaColorMode.Light : MokaColorMode.Dark,
			Accent = AccentIdFor(usable.AccentColor),
			StatusBar = StatusBarLooks.Of(usable.StatusBarStyle),
		};
	}

	public static AppearanceSettings Apply(AppearanceSettings settings, MokaAppearance picked)
	{
		ArgumentNullException.ThrowIfNull(settings);
		ArgumentNullException.ThrowIfNull(picked);
		return settings with
		{
			Mode = ModeFrom(picked.Mode, settings.Mode),
			AccentColor = AccentColorFrom(picked.Accent, settings.AccentColor),
			StatusBarStyle = picked.StatusBar is { } look ? StatusBarLooks.From(look) : settings.StatusBarStyle,
		};
	}

	public static ThemeMode ModeFrom(MokaColorMode picked, ThemeMode current) => picked switch
	{
		MokaColorMode.Light => ThemeMode.Light,
		MokaColorMode.Dark => ThemeMode.Dark,
		_ => current,
	};

	private static string AccentIdFor(string color)
	{
		foreach ((string id, _, string preset) in AccentPresets)
		{
			if (string.Equals(color, preset, StringComparison.OrdinalIgnoreCase))
			{
				return id;
			}
		}

		return CustomAccentId;
	}

	private static string AccentColorFrom(string? id, string current)
	{
		// Clicking the picked swatch clears it, which here means back to the default.
		if (id is null)
		{
			return AppearanceSettings.DefaultAccentColor;
		}

		foreach ((string presetId, _, string color) in AccentPresets)
		{
			if (presetId == id)
			{
				return color;
			}
		}

		return current;
	}
}
