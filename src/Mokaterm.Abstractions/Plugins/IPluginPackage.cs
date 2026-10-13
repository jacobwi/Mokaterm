namespace Mokaterm.Abstractions.Plugins;

/// <summary>
/// A <c>.mokaplugin</c> file that was read and checked but not installed: what the install dialog shows before the user
/// agrees. It holds the package in memory until it is installed or disposed.
/// </summary>
public interface IPluginPackage : IDisposable
{
	string Id { get; }

	string Name { get; }

	string Version { get; }

	string? Publisher { get; }

	string? Description { get; }

	bool HasCode { get; }

	IReadOnlyList<string> Hosts { get; }

	int ThemeCount { get; }

	IReadOnlyList<PluginPackageFile> Files { get; }

	/// <summary>SHA-256 over every file's path and hash, the value the user trusts when turning a code plugin on.</summary>
	string Hash { get; }

	/// <summary>The version already installed under the same id, or null.</summary>
	string? InstalledVersion { get; }

	/// <summary>Why this package cannot be used on this host or this version, or null when it can.</summary>
	string? Problem { get; }

	/// <summary>Where the package came from, or null for a file the user picked.</summary>
	PluginPackageOrigin? Origin { get; }
}
