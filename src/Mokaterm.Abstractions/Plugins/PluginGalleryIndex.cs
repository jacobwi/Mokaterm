namespace Mokaterm.Abstractions.Plugins;

/// <summary>The gallery's signed list of plugins, after its signature and every field were checked.</summary>
public sealed record PluginGalleryIndex
{
	/// <summary>When the gallery wrote the list.</summary>
	public required DateTimeOffset Generated { get; init; }

	/// <summary>Every plugin listed, by id.</summary>
	public required IReadOnlyList<PluginGalleryEntry> Plugins { get; init; }

	public PluginGalleryEntry? Find(string id) =>
		Plugins.FirstOrDefault(plugin => string.Equals(plugin.Id, id, StringComparison.Ordinal));
}
