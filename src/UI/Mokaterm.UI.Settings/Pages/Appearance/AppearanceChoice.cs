using Moka.Red.Core.Theming;
using Moka.Red.ThemeGen.Appearance;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Presentation;

namespace Mokaterm.UI.Settings.Pages.Appearance;

// Translates between AppearanceSettings and the MokaAppearance the page's panels take and give back.
internal static class AppearanceChoice
{
	private const string CustomAccentId = "custom";

	// Moka's own presets, tuned for contrast in both modes, with its red first under our name for it. A preset is stored as
	// its dark colour, which MokaTheme.WithAccent maps back to that exact preset; red is stored as our default, which
	// ThemeFactory leaves to the Red theme's own accent.
	private static readonly IReadOnlyList<MokaAccent> PresetAccents =
		[MokaAccent.Red with { Name = "Moka red" }, .. MokaAccent.Presets.Where(accent => accent.Id != MokaAccent.Red.Id)];

	// Only the design ThemeFactory builds, so the pictures show what the app will look like.
	public static IReadOnlyList<MokaAppearancePalette> Palettes { get; } = [MokaAppearancePalette.Red];

	public static MokaAppearanceText Words { get; } = new()
	{
		ColorMode = "Theme mode",
		Accent = "Accent color",
		StatusBar = "Status bar style",
		Popups = "Menus and popups",
		Lists = "Field lists",
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
			Toasts = ComponentLooks.Of(usable.ToastStyle),
			ToastStacking = ComponentLooks.Of(usable.ToastStack),
			Popups = ComponentLooks.Of(usable.PopupStyle),
			Lists = ComponentLooks.Of(usable.ListStyle),
			Buttons = ComponentLooks.Of(usable.ButtonStyle),
			Tooltips = ComponentLooks.Of(usable.TooltipStyle),
			TooltipArrows = usable.TooltipArrows,
			TooltipMotion = ComponentLooks.Of(usable.TooltipMotion),
			TooltipSize = ComponentLooks.Of(usable.TooltipSize),
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
			ToastStyle = picked.Toasts is { } toasts ? ComponentLooks.From(toasts) : settings.ToastStyle,
			ToastStack = picked.ToastStacking is { } stacking ? ComponentLooks.From(stacking) : settings.ToastStack,
			PopupStyle = picked.Popups is { } popups ? ComponentLooks.From(popups) : settings.PopupStyle,
			ListStyle = picked.Lists is { } lists ? ComponentLooks.From(lists) : settings.ListStyle,
			ButtonStyle = picked.Buttons is { } buttons ? ComponentLooks.From(buttons) : settings.ButtonStyle,
			TooltipStyle = picked.Tooltips is { } tooltips ? ComponentLooks.From(tooltips) : settings.TooltipStyle,
			TooltipArrows = picked.TooltipArrows ?? settings.TooltipArrows,
			TooltipMotion = picked.TooltipMotion is { } motion ? ComponentLooks.From(motion) : settings.TooltipMotion,
			TooltipSize = picked.TooltipSize is { } size ? ComponentLooks.From(size) : settings.TooltipSize,
		};
	}

	public static ThemeMode ModeFrom(MokaColorMode picked, ThemeMode current) => picked switch
	{
		MokaColorMode.Light => ThemeMode.Light,
		MokaColorMode.Dark => ThemeMode.Dark,
		_ => current,
	};

	// Either of a preset's colours is that preset, because WithAccent draws either as the preset itself.
	private static string AccentIdFor(string color)
	{
		if (string.Equals(color, AppearanceSettings.DefaultAccentColor, StringComparison.OrdinalIgnoreCase))
		{
			return MokaAccent.Red.Id;
		}

		foreach (MokaAccent preset in PresetAccents)
		{
			if (string.Equals(color, preset.Dark.Color, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(color, preset.Light.Color, StringComparison.OrdinalIgnoreCase))
			{
				return preset.Id;
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

		if (id == MokaAccent.Red.Id)
		{
			return AppearanceSettings.DefaultAccentColor;
		}

		foreach (MokaAccent preset in PresetAccents)
		{
			if (preset.Id == id)
			{
				return preset.Dark.Color;
			}
		}

		return current;
	}
}
