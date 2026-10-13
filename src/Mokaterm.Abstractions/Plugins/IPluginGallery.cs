namespace Mokaterm.Abstractions.Plugins;

/// <summary>
/// The plugin gallery: a signed list on the web and the packages it names. Nothing is asked of the gallery's host until
/// the Plugins page's Browse tab opens; after that the list is kept for the window (or circuit) and read again only on
/// a refresh. Only hosts where the user installs plugins register it, so a missing service means no Browse tab.
/// </summary>
public interface IPluginGallery
{
	/// <summary>Where the list is read from. The page names its host before anything is asked.</summary>
	Uri Index { get; }

	/// <summary>The list read in this window, or null until it is first read.</summary>
	PluginGalleryIndex? Current { get; }

	/// <summary>Raised, on any thread, once a list was read.</summary>
	event EventHandler? Changed;

	/// <summary>
	/// The list, read and checked against its signature the first time it is asked for, and again when
	/// <paramref name="refresh"/> is set.
	/// </summary>
	/// <exception cref="PluginGalleryException">The list could not be read or failed a check; the message says why.</exception>
	Task<PluginGalleryIndex> ReadIndexAsync(bool refresh, CancellationToken cancellationToken);

	/// <summary>
	/// Downloads one version of a plugin, checks it against the list (size, SHA-256, id, version, code or data) and reads
	/// it the way a picked file is read, with its origin for the install dialog. The caller owns the package.
	/// </summary>
	/// <exception cref="PluginGalleryException">
	/// The download failed, did not match the list, or is not a usable package; the message says why.
	/// </exception>
	Task<IPluginPackage> DownloadAsync(PluginGalleryEntry plugin, PluginGalleryVersion version, CancellationToken cancellationToken);
}
