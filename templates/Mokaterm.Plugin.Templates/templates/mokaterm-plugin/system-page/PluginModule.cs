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
		"A Kernel parameters page in the System view.",
		DisplayVersion.Of(typeof(PluginModule).Assembly));

	public void ConfigureServices(IServiceCollection services)
	{
		// Between the view's Processes (900) and Logs (1000) pages. The id is what the view navigates by; a later
		// registration with the same id replaces this one, so it should not be one Mokaterm or another plugin uses.
		services.AddSystemPage(new SystemPageDescriptor
		{
			Id = "kernel-parameters",
			Title = "Kernel parameters",
			Icon = MokatermIcons.Sliders,
			Component = typeof(KernelParametersPage),
			Order = 950,
		});

		// The page id, like the settings key, is plugin. and the plugin's id: the loader refuses a plugin that uses any other.
		services.AddSettingsPage(new SettingsPageDescriptor
		{
			Id = PluginSettings.SectionKey,
			Title = "MokatermPlugin1",
			Description = "Which kernel parameters the System view's page reads, and how often.",
			Group = SettingsPageGroup.Plugins,
			Icon = MokatermIcons.Plugin,
			Component = typeof(PluginSettingsPage),
			Keywords = "kernel parameters sysctl refresh",
		});
	}
}
