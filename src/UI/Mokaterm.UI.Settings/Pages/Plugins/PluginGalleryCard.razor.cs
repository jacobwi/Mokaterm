using Microsoft.AspNetCore.Components;
using Mokaterm.Abstractions.Plugins;

namespace Mokaterm.UI.Settings.Pages.Plugins;

/// <summary>
/// One plugin in the gallery: what it is, who made it, where its source is, and Install, Update or what is already
/// installed. The page does the download and the install.
/// </summary>
public sealed partial class PluginGalleryCard : ComponentBase
{
	[Parameter, EditorRequired]
	public PluginGalleryEntry Plugin { get; set; } = default!;

	/// <summary>The installed plugin with the same id, or null.</summary>
	[Parameter]
	public PluginRecord? Installed { get; set; }

	/// <summary>True while any download or change runs, so two never overlap.</summary>
	[Parameter]
	public bool Busy { get; set; }

	/// <summary>True while this plugin is being downloaded.</summary>
	[Parameter]
	public bool Downloading { get; set; }

	[Parameter]
	public EventCallback<PluginGalleryEntry> OnInstall { get; set; }

	[Parameter]
	public EventCallback<PluginGalleryEntry> OnOpenSource { get; set; }

	private PluginGalleryOffer Offer => PluginGalleryOffer.For(Plugin, Installed);

	// The version an install takes, or the newest listed when none is offered here, so the card still says what exists.
	private string? ShownVersion => Plugin.Newest?.Version ?? (Plugin.Versions.Count > 0 ? Plugin.Versions[0].Version : null);
}
