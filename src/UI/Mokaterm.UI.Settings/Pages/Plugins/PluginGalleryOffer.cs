using Mokaterm.Abstractions.Plugins;

namespace Mokaterm.UI.Settings.Pages.Plugins;

/// <summary>What a gallery card offers for one plugin, given what is installed.</summary>
/// <param name="Kind">What the card's button does.</param>
/// <param name="Version">The version Install or Update takes, or null when the button takes none.</param>
internal sealed record PluginGalleryOffer(PluginGalleryOfferKind Kind, PluginGalleryVersion? Version)
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

		// The record says what this start loaded, not what waits in the pending folder, so a version cannot be compared.
		if (installed.Pending is PluginPendingChange.Install or PluginPendingChange.Update)
		{
			return new PluginGalleryOffer(PluginGalleryOfferKind.Waiting, null);
		}

		return entry.UpdateFor(installed.Version) is { } update
			? new PluginGalleryOffer(PluginGalleryOfferKind.Update, update)
			: new PluginGalleryOffer(PluginGalleryOfferKind.Installed, null);
	}
}
