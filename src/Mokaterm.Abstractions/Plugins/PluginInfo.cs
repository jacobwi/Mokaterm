namespace Mokaterm.Abstractions.Plugins;

/// <summary>A plugin loaded at start. Every one is also resolvable as <c>IEnumerable&lt;PluginInfo&gt;</c>.</summary>
public sealed record PluginInfo(string Id, string Name, string Version, string AssemblyName)
{
	/// <summary>
	/// The folder served at <see cref="ContentPath"/>, or null for a plugin with no files for the page. Hosts read it;
	/// the page only ever sees the path.
	/// </summary>
	public string? WebRoot { get; init; }

	/// <summary>
	/// Where the plugin's static files are served, the same <c>_content/{assembly}/</c> a built-in module's are, so a
	/// module can move between the two without changing its script paths.
	/// </summary>
	public string ContentPath => $"_content/{AssemblyName}/";

	/// <summary>
	/// The plugin's scoped CSS bundle, or null when it has none. A host's own <c>*.styles.css</c> only holds the libraries
	/// it was built with, so the shell links this one itself.
	/// </summary>
	public string? StyleSheet { get; init; }
}
