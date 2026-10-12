namespace Mokaterm.Core.Plugins;

/// <summary>A plugin's <c>plugin.json</c>, after <see cref="PluginManifestReader"/> checked and trimmed it.</summary>
internal sealed record PluginManifest
{
	public string Id { get; init; } = "";

	public string Name { get; init; } = "";

	public string Version { get; init; } = "";

	public string? Publisher { get; init; }

	public string? Description { get; init; }

	/// <summary>The Mokaterm version the plugin was built against, major and minor ("0.2"). Required for code.</summary>
	public string? Sdk { get; init; }

	/// <summary>The file name of the assembly in <c>lib</c>, or null for a plugin that only carries data.</summary>
	public string? Assembly { get; init; }

	/// <summary>Lower-case host names from <see cref="Abstractions.Plugins.PluginHosts"/>; empty means every host.</summary>
	public IReadOnlyList<string> Hosts { get; init; } = [];

	public bool HasCode => Assembly is not null;
}
