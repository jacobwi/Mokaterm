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
	public static T Sanitize<T>(T section) where T : class, ISettingsSection => section switch
	{
		AppearanceSettings appearance => (T)(object)Sanitize(appearance),
		TerminalSettings terminal => (T)(object)Sanitize(terminal),
		SecuritySettings security => (T)(object)Sanitize(security),
		FileTransferSettings files => (T)(object)Sanitize(files),
		GeneralSettings general => (T)(object)Sanitize(general),
		UpdateSettings updates => (T)(object)Sanitize(updates),
		SessionLogSettings sessionLog => (T)(object)Sanitize(sessionLog),
		_ => section,
	};

	private static AppearanceSettings Sanitize(AppearanceSettings settings)
	{
		AppearanceSettings defaults = new();
		return settings.Clamped() with
		{
			Mode = Defined(settings.Mode, defaults.Mode),
			Density = Defined(settings.Density, defaults.Density),
		};
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
