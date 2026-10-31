using Mokaterm.Abstractions.Plugins;

namespace Mokaterm.UI.Settings.Pages.Plugins;

/// <summary>What a gallery card offers for one plugin, given what is installed.</summary>
/// <param name="Kind">What the card's button does.</param>
/// <param name="Version">The version Install or Update takes, or null when the button takes none.</param>
/// <param name="WaitingVersion">The version a waiting install or update brings, when the record names it.</param>
internal sealed record PluginGalleryOffer(PluginGalleryOfferKind Kind, PluginGalleryVersion? Version, string? WaitingVersion = null)
{
	/// <param name="entry">The plugin as the gallery lists it.</param>
	/// <param name="installed">The installed plugin with the same id, or null.</param>
	public static PluginGalleryOffer For(PluginGalleryEntry entry, PluginRecord? installed)
	{
		ArgumentNullException.ThrowIfNull(entry);

		// A plugin on its way out counts as not installed: installing it again takes back the removal.
		if (installed is null || installed.Pending == PluginPendingChange.Remove)
		{
			return entry.Newest is { } newest
				? new PluginGalleryOffer(PluginGalleryOfferKind.Install, newest)
				: new PluginGalleryOffer(PluginGalleryOfferKind.Unavailable, null);
		}

		// A waiting install or update cannot be compared against the gallery yet: the record's version is what this
		// start loaded, and the folder names the one the next start will.
		if (installed.Pending is PluginPendingChange.Install or PluginPendingChange.Update)
		{
			return new PluginGalleryOffer(PluginGalleryOfferKind.Waiting, null, installed.PendingVersion);
		}

		return entry.UpdateFor(installed.Version) is { } update
			? new PluginGalleryOffer(PluginGalleryOfferKind.Update, update)
			: new PluginGalleryOffer(PluginGalleryOfferKind.Installed, null);
	}
}
