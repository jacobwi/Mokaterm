using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.Core.Terminal;

/// <summary>Built-in themes followed by the user's custom themes, rebuilt whenever the terminal settings change.</summary>
internal sealed class TerminalThemeCatalog : ITerminalThemeCatalog, IDisposable
{
	private readonly ISettingsService _settings;
	private volatile ThemeSet _themes;

	public TerminalThemeCatalog(ISettingsService settings)
	{
		_settings = settings;
		_settings.Changed += OnSettingsChanged;
		_themes = Build(settings.Get<TerminalSettings>().CustomThemes);
	}

	public TerminalTheme Default => _themes.Default;

	public IReadOnlyList<TerminalTheme> Themes => _themes.All;

	public TerminalTheme Get(string? id) =>
		id is not null && _themes.ById.TryGetValue(id, out TerminalTheme? theme) ? theme : _themes.Default;

	public void Dispose() => _settings.Changed -= OnSettingsChanged;

	private static ThemeSet Build(IReadOnlyList<TerminalTheme> customThemes)
	{
		Dictionary<string, TerminalTheme> byId = new(StringComparer.OrdinalIgnoreCase);
		List<TerminalTheme> all = [];
		foreach (TerminalTheme theme in BuiltInTerminalThemes.All)
		{
			byId.Add(theme.Id, theme);
			all.Add(theme);
		}

		// Custom themes cannot shadow a built-in id; the settings sanitizer already dropped incomplete ones.
		foreach (TerminalTheme theme in customThemes)
		{
			TerminalTheme custom = theme.IsCustom ? theme : theme with { IsCustom = true };
			if (byId.TryAdd(custom.Id, custom))
			{
				all.Add(custom);
			}
		}

		return new ThemeSet(all, byId, byId[BuiltInTerminalThemes.DefaultId]);
	}

	private void OnSettingsChanged(string sectionKey)
	{
		if (sectionKey == TerminalSettings.SectionKey)
		{
			_themes = Build(_settings.Get<TerminalSettings>().CustomThemes);
		}
	}

	private sealed record ThemeSet(IReadOnlyList<TerminalTheme> All, IReadOnlyDictionary<string, TerminalTheme> ById, TerminalTheme Default);
}
