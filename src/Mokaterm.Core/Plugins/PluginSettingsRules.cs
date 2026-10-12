using Mokaterm.Abstractions.Settings;

namespace Mokaterm.Core.Plugins;

/// <summary>
/// The names a plugin's settings may use: every settings section key and settings page id starts with
/// <c>plugin.&lt;id&gt;</c>. Built-in sections never start with <c>plugin.</c>, so a plugin cannot read or overwrite
/// Mokaterm's own settings by picking a name such as <c>ssh</c>, and two plugins stay apart because their ids differ.
/// Code plugins run with full trust, so this catches mistakes, not intent.
/// </summary>
public static class PluginSettingsRules
{
	public const string Prefix = "plugin.";

	/// <summary>The key, or page id, a plugin's settings start with: <c>plugin.moka.sample</c>.</summary>
	public static string NameFor(string pluginId)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
		return Prefix + pluginId;
	}

	/// <summary>True for <c>plugin.&lt;id&gt;</c> itself and for <c>plugin.&lt;id&gt;.anything</c>.</summary>
	public static bool Belongs(string pluginId, string name)
	{
		ArgumentNullException.ThrowIfNull(name);
		string own = NameFor(pluginId);
		return name.Equals(own, StringComparison.Ordinal) || name.StartsWith(own + ".", StringComparison.Ordinal);
	}

	/// <summary>
	/// Why the sections and pages a plugin registered cannot be kept, or null. <paramref name="sectionsInUse"/> are the
	/// sections registered before it; the same section registered twice is one section, so only a key that some other
	/// type already holds is a clash. A plugin whose id is a prefix of another's (<c>moka</c> and <c>moka.sample</c>)
	/// could name the same key, which this catches too.
	/// </summary>
	public static string? Problem(
		string pluginId,
		IEnumerable<SettingsSectionType> sections,
		IEnumerable<string> pageIds,
		IReadOnlyDictionary<string, Type> sectionsInUse)
	{
		ArgumentNullException.ThrowIfNull(sections);
		ArgumentNullException.ThrowIfNull(pageIds);
		ArgumentNullException.ThrowIfNull(sectionsInUse);
		string own = NameFor(pluginId);
		foreach (SettingsSectionType section in sections)
		{
			if (!Belongs(pluginId, section.Key))
			{
				return $"Its settings are stored under \"{section.Key}\". A plugin's settings keys start with {own}, so it cannot overwrite Mokaterm's own settings or another plugin's.";
			}

			if (sectionsInUse.TryGetValue(section.Key, out Type? holder) && holder != section.Type)
			{
				return $"Its settings key \"{section.Key}\" is already used by another plugin.";
			}
		}

		foreach (string pageId in pageIds)
		{
			if (!Belongs(pluginId, pageId))
			{
				return $"Its settings page is named \"{pageId}\". A plugin's settings pages are named from {own}, so it cannot replace one of Mokaterm's.";
			}
		}

		return null;
	}
}
