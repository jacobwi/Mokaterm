using Mokaterm.Abstractions.Plugins;
using Mokaterm.UI.Common.Contributions;

namespace Mokaterm.UI.Settings.Pages.Plugins;

/// <summary>Which settings page belongs to a loaded plugin: the first whose component comes from the plugin's assembly.</summary>
internal static class PluginSettingsPages
{
	public static SettingsPageDescriptor? For(PluginInfo plugin, IEnumerable<SettingsPageDescriptor> pages)
	{
		ArgumentNullException.ThrowIfNull(plugin);
		ArgumentNullException.ThrowIfNull(pages);
		return pages.FirstOrDefault(page =>
			string.Equals(page.Component.Assembly.GetName().Name, plugin.AssemblyName, StringComparison.OrdinalIgnoreCase));
	}
}
