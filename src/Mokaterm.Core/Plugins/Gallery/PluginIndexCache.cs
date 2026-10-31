using Mokaterm.Abstractions.Plugins;

namespace Mokaterm.Core.Plugins.Gallery;

/// <summary>
/// The newest plugin list this app run has read, whatever read it: the startup check, the Browse tab or a Check for
/// updates. The read is always the gallery client's own (signature verified before a field of it is used); this only
/// remembers the result, so a window that has not asked yet still shows update chips from the list another part of the
/// app already trusted. Nothing here is written anywhere.
/// </summary>
internal sealed class PluginIndexCache
{
	private readonly object _gate = new();
	private PluginGalleryIndex? _latest;

	public PluginGalleryIndex? Latest
	{
		get
		{
			lock (_gate)
			{
				return _latest;
			}
		}
	}

	public void Keep(PluginGalleryIndex index)
	{
		ArgumentNullException.ThrowIfNull(index);
		lock (_gate)
		{
			_latest = index;
		}
	}
}

/// <summary>One installed plugin with a newer version the gallery offers for this Mokaterm.</summary>
/// <param name="PluginId">The plugin's id, as the installed record and the list both name it.</param>
/// <param name="Name">The plugin's name, as the notice reads it.</param>
/// <param name="InstalledVersion">The version this start loaded.</param>
/// <param name="NewVersion">The version the list offers.</param>
internal sealed record PluginUpdateOffer(string PluginId, string Name, string InstalledVersion, string NewVersion);
