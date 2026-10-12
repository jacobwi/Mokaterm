using Microsoft.Extensions.DependencyInjection;
using Mokaterm.Abstractions.Modules;
using Mokaterm.Abstractions.Plugins;

namespace Mokaterm.Core.Plugins;

public static class PluginBuilderExtensions
{
	/// <summary>
	/// Adds each loaded plugin's module like a built-in one, publishes its <see cref="PluginInfo"/> for the page's files
	/// and styles, and registers the <see cref="IPluginManager"/> the Plugins page works through. Every host calls it,
	/// with an empty catalog when it has no plugins folder, so the page always has a manager.
	/// </summary>
	public static IMokatermBuilder AddPlugins(this IMokatermBuilder builder, PluginCatalog catalog)
	{
		ArgumentNullException.ThrowIfNull(builder);
		ArgumentNullException.ThrowIfNull(catalog);

		// AddModule quietly skips a module id it has seen, which for a plugin would be a tool that never appears with
		// nothing saying why. Built-in modules are added before plugins, so a clash with one is caught here too.
		HashSet<string> moduleIds = new(
			builder.Services
				.Where(descriptor => descriptor.ServiceType == typeof(ModuleInfo) && !descriptor.IsKeyedService)
				.Select(descriptor => descriptor.ImplementationInstance)
				.OfType<ModuleInfo>()
				.Select(info => info.Id),
			StringComparer.OrdinalIgnoreCase);
		foreach (LoadedPlugin plugin in catalog.Loaded.ToList())
		{
			if (ModuleId(plugin) is not { } moduleId)
			{
				catalog.Reject(plugin.Info.Id, "Its module does not say what it is (no module info), so it was not added.");
				continue;
			}

			if (!moduleIds.Add(moduleId))
			{
				catalog.Reject(plugin.Info.Id, $"Its module id \"{moduleId}\" is already used by Mokaterm or another plugin, so it was not added.");
				continue;
			}

			builder.AddModule(plugin.Module);
			builder.Services.AddSingleton(plugin.Info);
		}

		builder.Services.AddSingleton(catalog);
		builder.Services.AddSingleton(new PluginThemeSet(catalog.Themes));
		builder.Services.AddSingleton<IPluginManager>(_ => new PluginManager(catalog));
		return builder;
	}

	private static string? ModuleId(LoadedPlugin plugin)
	{
		try
		{
			return plugin.Module.Info?.Id is { Length: > 0 } id ? id : null;
		}
		catch (Exception ex) when (ex is not OutOfMemoryException)
		{
			// Plugin code; whatever it throws is a plugin that cannot be added, not a host that cannot start.
			return null;
		}
	}
}
