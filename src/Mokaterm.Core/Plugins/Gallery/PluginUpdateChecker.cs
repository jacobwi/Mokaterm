using System.Net.Http;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Plugins;

namespace Mokaterm.Core.Plugins.Gallery;

/// <summary>
/// Reads the gallery's signed list and says which installed plugins have newer versions there. The startup check calls
/// it once behind its setting and shows a notice for what it finds; installing stays in Settings, Plugins. A plugin
/// the list does not name — installed from a file, or from a list it has left — simply has no update, and a version
/// the list withdrew is never offered (the list's own offered versions are the ones this compares with).
/// </summary>
internal sealed class PluginUpdateChecker(
	PluginGalleryClient client,
	PluginIndexCache cache,
	IPluginManager plugins,
	IAppEnvironment environment,
	ILogger<PluginUpdateChecker> logger)
{
	private readonly object _gate = new();
	private readonly string _hostName = PluginHosts.For(environment.Kind);
	private IReadOnlyList<PluginUpdateOffer>? _latest;

	/// <summary>What the last check found, newest read; null before the first check.</summary>
	public IReadOnlyList<PluginUpdateOffer>? Latest
	{
		get
		{
			lock (_gate)
			{
				return _latest;
			}
		}
	}

	/// <summary>
	/// Reads the list (the client verifies the signature and keeps the cache) and compares it with the installed
	/// plugins. Never throws: a list that cannot be read is a check that did not happen, logged and that is all.
	/// </summary>
	public async Task<IReadOnlyList<PluginUpdateOffer>?> CheckAsync(CancellationToken cancellationToken)
	{
		PluginGalleryIndex index;
		try
		{
			index = await client.ReadIndexAsync(_hostName, cancellationToken);
		}
		catch (Exception ex) when (ex is PluginGalleryException or HttpRequestException or IOException or InvalidOperationException or OperationCanceledException)
		{
			logger.LogInformation("The plugin update check read no list: {Failure}", ex.Message);
			return null;
		}

		IReadOnlyList<PluginUpdateOffer> offers = For(index, plugins.Plugins);
		lock (_gate)
		{
			_latest = offers;
		}

		cache.Keep(index);
		return offers;
	}

	/// <summary>
	/// The pure comparison: every installed plugin, that is not on its way out, whose entry offers a version newer
	/// than the one this start loaded — offered meaning the list's own rule, which leaves out withdrawn versions and
	/// ones this Mokaterm cannot load.
	/// </summary>
	public static IReadOnlyList<PluginUpdateOffer> For(PluginGalleryIndex index, IReadOnlyList<PluginRecord> installed)
	{
		ArgumentNullException.ThrowIfNull(index);
		ArgumentNullException.ThrowIfNull(installed);
		List<PluginUpdateOffer> offers = [];
		foreach (PluginRecord plugin in installed)
		{
			if (plugin.Pending != PluginPendingChange.Remove
				&& index.Find(plugin.Id) is { } entry
				&& entry.UpdateFor(plugin.Version) is { } update)
			{
				offers.Add(new PluginUpdateOffer(plugin.Id, entry.Name, plugin.Version, update.Version));
			}
		}

		return offers;
	}
}
