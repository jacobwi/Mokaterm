namespace Mokaterm.Core.Plugins;

/// <summary>The contents of <c>plugins.json</c>.</summary>
internal sealed record PluginState
{
	public Dictionary<string, PluginStateEntry> Plugins { get; init; } = new(StringComparer.Ordinal);

	/// <summary>Plugins to delete at the next start, before anything loads from their folders.</summary>
	public List<string> Remove { get; init; } = [];
}
