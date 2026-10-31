namespace Mokaterm.Abstractions.Plugins;

/// <summary>
/// The download a window's gallery is running, or finished with and nothing has taken yet. It belongs to the window
/// rather than the page, so leaving the Plugins page neither stops the download nor loses the package: coming back
/// shows how far it is, and opens the install dialog when it finished in the meantime.
/// </summary>
public sealed record PluginGalleryDownload
{
	public required string PluginId { get; init; }

	/// <summary>The plugin's name, as the card the download started from shows it.</summary>
	public required string PluginName { get; init; }

	public required string Version { get; init; }

	/// <summary>True once the download finished, whichever way it went.</summary>
	public bool Done { get; init; }

	/// <summary>The downloaded package, once it succeeded and before anything takes it.</summary>
	public IPluginPackage? Package { get; init; }

	/// <summary>Why the download failed, in a sentence for the user, or null.</summary>
	public string? Problem { get; init; }
}
