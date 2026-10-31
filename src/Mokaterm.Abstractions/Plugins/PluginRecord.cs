namespace Mokaterm.Abstractions.Plugins;

/// <summary>An installed plugin as the Plugins page lists it.</summary>
public sealed record PluginRecord
{
	public required string Id { get; init; }

	public required string Name { get; init; }

	public required string Version { get; init; }

	public string? Publisher { get; init; }

	public string? Description { get; init; }

	/// <summary>True when the plugin carries an assembly; false for one that only carries data, such as themes.</summary>
	public bool HasCode { get; init; }

	/// <summary>The hosts the plugin says it supports (<see cref="PluginHosts"/>); empty means every host.</summary>
	public IReadOnlyList<string> Hosts { get; init; } = [];

	public PluginStatus Status { get; init; }

	/// <summary>Why the plugin is not working, in a sentence for the user, or null.</summary>
	public string? Problem { get; init; }

	/// <summary>Terminal themes the plugin carries.</summary>
	public int ThemeCount { get; init; }

	/// <summary>True when the plugin is turned on for the next start, whatever this start did with it.</summary>
	public bool IsEnabled { get; init; }

	/// <summary>A change waits for the next start: an install, an update, a removal or a switch on or off.</summary>
	public PluginPendingChange Pending { get; init; }

	/// <summary>
	/// The version the next start loads, when a waiting install or update brings one. The record's own
	/// <see cref="Version"/> says what this start loaded, which is not it.
	/// </summary>
	public string? PendingVersion { get; init; }
}
