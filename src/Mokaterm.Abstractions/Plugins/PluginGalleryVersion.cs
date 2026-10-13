namespace Mokaterm.Abstractions.Plugins;

/// <summary>One version of a plugin, as the gallery's <c>index.json</c> lists it.</summary>
public sealed record PluginGalleryVersion
{
	/// <summary>The version, as in the package's <c>plugin.json</c>.</summary>
	public required string Version { get; init; }

	/// <summary>The Mokaterm major and minor the package was built against, such as 0.2.</summary>
	public required string Sdk { get; init; }

	/// <summary>The https address of the <c>.mokaplugin</c> file.</summary>
	public required Uri Url { get; init; }

	/// <summary>SHA-256 of the package file, in lower-case hex.</summary>
	public required string Sha256 { get; init; }

	/// <summary>The package file's size in bytes.</summary>
	public required long Size { get; init; }

	/// <summary>The full commit in the plugin's source that the package was built from.</summary>
	public required string Commit { get; init; }

	public required DateTimeOffset Published { get; init; }

	/// <summary>Why the version should no longer be used, in a sentence, or null. A withdrawn version is never offered.</summary>
	public string? Withdrawn { get; init; }
}
