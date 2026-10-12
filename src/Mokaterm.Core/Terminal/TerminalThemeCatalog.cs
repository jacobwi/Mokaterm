using System.Text;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.Core.Plugins;

namespace Mokaterm.Core.Terminal;

/// <summary>
/// Built-in themes, then the themes enabled plugins carry, then the user's custom themes; rebuilt whenever the terminal
/// settings change.
/// </summary>
internal sealed class TerminalThemeCatalog : ITerminalThemeCatalog, IDisposable
{
	private readonly ISettingsService _settings;
	private readonly IReadOnlyList<TerminalTheme> _pluginThemes;
	private volatile ThemeSet _themes;

	public TerminalThemeCatalog(ISettingsService settings, ITerminalThemeConverter? converter = null, PluginThemeSet? pluginThemes = null)
	{
		_settings = settings;
		_pluginThemes = converter is null || pluginThemes is null ? [] : ReadPluginThemes(converter, pluginThemes);
		_settings.Changed += OnSettingsChanged;
		_themes = Build(settings.Get<TerminalSettings>().CustomThemes);
	}

	public TerminalTheme Default => _themes.Default;

	public IReadOnlyList<TerminalTheme> Themes => _themes.All;

	public TerminalTheme Get(string? id) =>
		id is not null && _themes.ById.TryGetValue(id, out TerminalTheme? theme) ? theme : _themes.Default;

	public void Dispose() => _settings.Changed -= OnSettingsChanged;

	private ThemeSet Build(IReadOnlyList<TerminalTheme> customThemes)
	{
		Dictionary<string, TerminalTheme> byId = new(StringComparer.OrdinalIgnoreCase);
		List<TerminalTheme> all = [];
		foreach (TerminalTheme theme in BuiltInTerminalThemes.All)
		{
			byId.Add(theme.Id, theme);
			all.Add(theme);
		}

		foreach (TerminalTheme theme in _pluginThemes)
		{
			if (byId.TryAdd(theme.Id, theme))
			{
				all.Add(theme);
			}
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

	// A plugin theme's id comes from the plugin and the file, so a terminal set to it finds it again after a restart; the
	// import's own ids are new every time. They are not custom: the user cannot edit or delete what a plugin owns.
	private static List<TerminalTheme> ReadPluginThemes(ITerminalThemeConverter converter, PluginThemeSet set)
	{
		List<TerminalTheme> themes = [];
		foreach (PluginThemeFile file in set.Files)
		{
			TerminalThemeImport import = converter.Import(file.Text, file.FileName);
			if (import.Theme is { } theme)
			{
				themes.Add(theme with { Id = $"plugin.{file.PluginId}.{Slug(Path.GetFileNameWithoutExtension(file.FileName))}", IsCustom = false });
			}
		}

		return themes;
	}

	private static string Slug(string name)
	{
		StringBuilder slug = new(name.Length);
		foreach (char c in name.ToLowerInvariant())
		{
			slug.Append(char.IsAsciiLetterOrDigit(c) ? c : '-');
		}

		return slug.ToString();
	}

	private sealed record ThemeSet(IReadOnlyList<TerminalTheme> All, IReadOnlyDictionary<string, TerminalTheme> ById, TerminalTheme Default);
}
