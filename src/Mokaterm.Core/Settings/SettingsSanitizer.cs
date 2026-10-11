using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.Core.Terminal;

namespace Mokaterm.Core.Settings;

/// <summary>
/// Pulls built-in settings sections back into their valid ranges, so a hand-edited or damaged settings.json cannot
/// break the app. Every range comes from the section's own <c>Clamped()</c>, the same one its settings page saves
/// through, so a value this keeps is one the page can show. What is left here is what a range cannot express: an enum
/// that names nothing, a blank string, a list with holes in it. Sections from modules pass through unchanged.
/// </summary>
internal static class SettingsSanitizer
{
	public static T Sanitize<T>(T section) where T : class, ISettingsSection => (T)SanitizeSection(section);

	/// <summary>
	/// The same for a section whose type is only known at run time, such as one read from an imported settings file.
	/// </summary>
	public static object SanitizeSection(object section) => section switch
	{
		AppearanceSettings appearance => Sanitize(appearance),
		TerminalSettings terminal => Sanitize(terminal),
		SecuritySettings security => Sanitize(security),
		FileTransferSettings files => Sanitize(files),
		GeneralSettings general => Sanitize(general),
		UpdateSettings updates => Sanitize(updates),
		SessionLogSettings sessionLog => Sanitize(sessionLog),
		CommandRunSettings commandRun => commandRun.Clamped(),
		TerminalTriggerSettings triggers => Sanitize(triggers),
		_ => section,
	};

	private static AppearanceSettings Sanitize(AppearanceSettings settings)
	{
		AppearanceSettings defaults = new();
		return settings.Clamped() with
		{
			Mode = Defined(settings.Mode, defaults.Mode),
			Density = Defined(settings.Density, defaults.Density),
			ToolbarLayout = Defined(settings.ToolbarLayout, defaults.ToolbarLayout),
			HiddenToolbarButtons = ToolbarButtonIds(settings.HiddenToolbarButtons),
		};
	}

	/// <summary>Blank, overlong and repeated ids are dropped, and the list is capped, so a hand edit cannot grow it.</summary>
	private static IReadOnlyList<string> ToolbarButtonIds(IReadOnlyList<string>? ids)
	{
		if (ids is null)
		{
			return [];
		}

		List<string> kept = [];
		HashSet<string> seen = new(StringComparer.Ordinal);
		foreach (string? id in ids)
		{
			string trimmed = id?.Trim() ?? "";
			if (trimmed.Length is > 0 and <= AppearanceSettings.MaxToolbarButtonIdLength && seen.Add(trimmed))
			{
				kept.Add(trimmed);
				if (kept.Count == AppearanceSettings.MaxHiddenToolbarButtons)
				{
					break;
				}
			}
		}

		return kept.Count == ids.Count && kept.SequenceEqual(ids, StringComparer.Ordinal) ? ids : kept;
	}

	private static TerminalSettings Sanitize(TerminalSettings settings)
	{
		TerminalSettings defaults = new();
		return settings.Clamped() with
		{
			ThemeId = NonBlank(settings.ThemeId, defaults.ThemeId),
			FontFamily = TerminalFontFamily.IsValid(settings.FontFamily) ? settings.FontFamily : defaults.FontFamily,
			CursorStyle = Defined(settings.CursorStyle, defaults.CursorStyle),
			RightClickAction = Defined(settings.RightClickAction, defaults.RightClickAction),
			Bell = Defined(settings.Bell, defaults.Bell),
			TerminalType = NonBlank(settings.TerminalType, defaults.TerminalType),
			CustomThemes = SanitizeThemes(settings.CustomThemes),
		};
	}

	private static SecuritySettings Sanitize(SecuritySettings settings)
	{
		SecuritySettings defaults = new();
		return settings.Clamped() with
		{
			HostKeyPolicy = Defined(settings.HostKeyPolicy, defaults.HostKeyPolicy),
		};
	}

	private static FileTransferSettings Sanitize(FileTransferSettings settings)
	{
		FileTransferSettings defaults = new();
		return settings.Clamped() with
		{
			Placement = Defined(settings.Placement, defaults.Placement),
			Overwrite = Defined(settings.Overwrite, defaults.Overwrite),
			DownloadDirectory = string.IsNullOrWhiteSpace(settings.DownloadDirectory) ? null : settings.DownloadDirectory,
		};
	}

	private static GeneralSettings Sanitize(GeneralSettings settings) => settings.Clamped();

	private static UpdateSettings Sanitize(UpdateSettings settings) => settings with
	{
		Feed = UpdateSettings.NormalizeFeed(settings.Feed),
	};

	private static SessionLogSettings Sanitize(SessionLogSettings settings)
	{
		SessionLogSettings defaults = new();
		return settings with
		{
			Folder = SessionLogSettings.NormalizeFolder(settings.Folder),
			SizeLimitKilobytes = Math.Clamp(
				settings.SizeLimitKilobytes,
				SessionLogSettings.MinSizeLimitKilobytes,
				SessionLogSettings.MaxSizeLimitKilobytes),
			Format = Defined(settings.Format, defaults.Format),
		};
	}

	/// <summary>
	/// The count and the pattern length come from <see cref="TerminalTriggerSettings.Clamped"/>. Here a color that names
	/// nothing goes back to none, and a rule without an id, or with one an earlier rule already has, gets a new one, since
	/// the settings page tells rules apart by it.
	/// </summary>
	private static TerminalTriggerSettings Sanitize(TerminalTriggerSettings settings)
	{
		TerminalTriggerSettings clamped = settings.Clamped();
		HashSet<Guid> ids = [];
		List<TerminalTrigger> rules = new(clamped.Rules.Count);
		bool changed = false;
		foreach (TerminalTrigger rule in clamped.Rules)
		{
			TerminalTrigger usable = rule;
			if (!Enum.IsDefined(usable.Highlight))
			{
				usable = usable with { Highlight = TriggerColor.None };
			}

			if (!ids.Add(usable.Id) || usable.Id == Guid.Empty)
			{
				usable = usable with { Id = Guid.NewGuid() };
				ids.Add(usable.Id);
			}

			changed |= !ReferenceEquals(usable, rule);
			rules.Add(usable);
		}

		return changed ? clamped with { Rules = rules } : clamped;
	}

	/// <summary>Drops custom themes a hand edit left without an id, name or color, and later duplicates of an id.</summary>
	private static IReadOnlyList<TerminalTheme> SanitizeThemes(IReadOnlyList<TerminalTheme>? themes)
	{
		if (themes is null)
		{
			return [];
		}

		HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
		List<TerminalTheme> usable = new(themes.Count);
		foreach (TerminalTheme? theme in themes)
		{
			if (TerminalThemeRules.IsComplete(theme) && ids.Add(theme.Id))
			{
				usable.Add(theme);
			}
		}

		// Returning the same list keeps record equality, so an update that changes nothing is recognized as such.
		return usable.Count == themes.Count ? themes : usable;
	}

	private static TEnum Defined<TEnum>(TEnum value, TEnum fallback) where TEnum : struct, Enum =>
		Enum.IsDefined(value) ? value : fallback;

	private static string NonBlank(string? value, string fallback) =>
		string.IsNullOrWhiteSpace(value) ? fallback : value;
}
