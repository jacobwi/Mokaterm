namespace Mokaterm.UI.Settings.Pages.Plugins;

/// <summary>What a gallery card's button does.</summary>
internal enum PluginGalleryOfferKind
{
	/// <summary>Not installed: Install takes the newest offered version.</summary>
	Install,

	/// <summary>Installed, and the gallery offers a newer version.</summary>
	Update,

	/// <summary>Installed, and nothing newer is offered.</summary>
	Installed,

	/// <summary>An install or update of it waits for the next start.</summary>
	Waiting,

	/// <summary>No version is offered for this Mokaterm or this host.</summary>
	Unavailable,
}
