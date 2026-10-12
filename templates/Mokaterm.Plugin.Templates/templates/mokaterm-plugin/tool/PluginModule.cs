using Microsoft.Extensions.DependencyInjection;
using Mokaterm.Abstractions.Modules;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Plugins;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.Icons;
using MokatermPlugin1;

[assembly: MokatermPlugin(typeof(PluginModule))]

namespace MokatermPlugin1;

/// <summary>
/// What the plugin adds to Mokaterm. The host finds this class through the attribute above, nothing is scanned, and
/// calls <see cref="ConfigureServices"/> once at start, the way it calls its own modules.
/// </summary>
public sealed class PluginModule : IMokatermModule
{
	// The module id has to be unique among Mokaterm's modules and every other plugin's, which the plugin id already is.
	public ModuleInfo Info { get; } = new(
		"PLUGIN_ID",
		"MokatermPlugin1",
		"A password generator in the Tools menu.",
		DisplayVersion.Of(typeof(PluginModule).Assembly));

	public void ConfigureServices(IServiceCollection services)
	{
		// The id names the tool's palette command and any shortcut bound to it, so it stays the same once released. A later
		// registration with the same id replaces this one, so it should not be one Mokaterm or another plugin uses.
		services.AddAppTool(new AppToolDescriptor
		{
			Id = "password-generator",
			Title = "Password generator",
			Icon = MokatermIcons.Key,
			Description = "A random password of the length and symbols set in the plugin's settings",
			Component = typeof(PasswordGeneratorTool),
		});

		// The page id, like the settings key, is plugin. and the plugin's id: the loader refuses a plugin that uses any other.
		services.AddSettingsPage(new SettingsPageDescriptor
		{
			Id = PluginSettings.SectionKey,
			Title = "MokatermPlugin1",
			Description = "The length and the symbols of the passwords the generator makes.",
			Group = SettingsPageGroup.Plugins,
			Icon = MokatermIcons.Plugin,
			Component = typeof(PluginSettingsPage),
			Keywords = "password generator length symbols",
		});
	}
}
