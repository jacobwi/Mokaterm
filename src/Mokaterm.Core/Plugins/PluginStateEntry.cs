namespace Mokaterm.Core.Plugins;

/// <summary>One plugin's line in <c>plugins.json</c>.</summary>
internal sealed record PluginStateEntry
{
	public bool Enabled { get; init; }

	/// <summary>The <see cref="PluginHash"/> of the files the user trusted, for a code plugin; null for data.</summary>
	public string? Trusted { get; init; }
}
