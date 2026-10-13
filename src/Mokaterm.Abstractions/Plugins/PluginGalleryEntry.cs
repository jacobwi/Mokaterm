namespace Mokaterm.Abstractions.Plugins;

/// <summary>
/// One plugin in the gallery's index. <see cref="Offered"/> holds the versions this build can install, which the gallery
/// works out once: built for this Mokaterm, meant for this host, and not withdrawn.
/// </summary>
public sealed record PluginGalleryEntry
{
	public required string Id { get; init; }

	public required string Name { get; init; }

	public required string Publisher { get; init; }

	public required string Description { get; init; }

	/// <summary>Category ids such as <c>server-tools</c> or <c>themes</c>.</summary>
	public required IReadOnlyList<string> Categories { get; init; }

	/// <summary>The https address of the repository the packages are built from.</summary>
	public required Uri Source { get; init; }

	/// <summary>An SPDX license identifier.</summary>
	public required string License { get; init; }

	/// <summary>True for the gallery's own plugins, whose source lives in the gallery repository.</summary>
	public required bool Verified { get; init; }

	/// <summary>False for a plugin that only carries data, such as a theme pack.</summary>
	public required bool HasCode { get; init; }

	/// <summary>The hosts the plugin supports (<see cref="PluginHosts"/>); empty means every host.</summary>
	public required IReadOnlyList<string> Hosts { get; init; }

	/// <summary>Every version listed, newest first, withdrawn ones included.</summary>
	public required IReadOnlyList<PluginGalleryVersion> Versions { get; init; }

	/// <summary>The versions this build can install, newest first.</summary>
	public required IReadOnlyList<PluginGalleryVersion> Offered { get; init; }

	/// <summary>Why no version is offered, in a sentence for the user, or null when one is.</summary>
	public string? Unavailable { get; init; }

	/// <summary>The version an install takes, or null when none is offered.</summary>
	public PluginGalleryVersion? Newest => Offered.Count > 0 ? Offered[0] : null;

	/// <summary>The newest offered version when it is newer than <paramref name="installedVersion"/>, or null.</summary>
	public PluginGalleryVersion? UpdateFor(string installedVersion) =>
		Newest is { } newest && PluginVersions.IsNewer(newest.Version, installedVersion) ? newest : null;

	/// <summary>The listed version <paramref name="installedVersion"/> when it was withdrawn, or null.</summary>
	public PluginGalleryVersion? WithdrawnFor(string installedVersion) =>
		Versions.FirstOrDefault(version => version.Withdrawn is not null && string.Equals(version.Version, installedVersion, StringComparison.Ordinal));
}
