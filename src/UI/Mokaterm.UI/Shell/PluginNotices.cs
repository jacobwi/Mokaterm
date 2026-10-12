using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Plugins;

namespace Mokaterm.UI.Shell;

/// <summary>
/// Says when this start skipped plugins or one did not load, which otherwise shows only as a tool that is missing. Like
/// every notice it waits for the vault to be unlocked.
/// </summary>
internal sealed class PluginNotices
{
	private readonly IPluginManager? _plugins;
	private readonly IUserInteraction _interaction;
	private bool _shown;

	// Optional: a host that registers no plugin manager simply has nothing to say.
	public PluginNotices(IEnumerable<IPluginManager> plugins, IUserInteraction interaction)
	{
		_plugins = plugins.LastOrDefault();
		_interaction = interaction;
	}

	/// <summary>Raises the notices once per window or circuit. The shell calls it when it starts.</summary>
	public void Show()
	{
		if (_shown || _plugins is null)
		{
			return;
		}

		_shown = true;
		if (_plugins.IsSafeStart)
		{
			if (_plugins.Plugins.Any(plugin => plugin.IsEnabled))
			{
				_interaction.Notify(
					NoticeSeverity.Warning,
					"The last start ended while loading plugins, or MOKATERM_NO_PLUGINS asked for this. If a plugin is the cause, turn it off in Settings, Plugins, then restart.",
					"Mokaterm started without its plugins");
			}

			return;
		}

		List<PluginRecord> failed = [.. _plugins.Plugins.Where(plugin => plugin.Status is PluginStatus.Failed or PluginStatus.Changed)];
		if (failed is [PluginRecord one])
		{
			_interaction.Notify(NoticeSeverity.Warning, one.Problem ?? "Settings, Plugins says why.", $"{one.Name} did not load");
		}
		else if (failed.Count > 1)
		{
			_interaction.Notify(NoticeSeverity.Warning, "Settings, Plugins says why for each.", $"{failed.Count} plugins did not load");
		}
	}
}
