using System.Globalization;
using Moka.Red.Core.Enums;
using Moka.Red.Core.Icons;
using Mokaterm.Abstractions.Plugins;
using Mokaterm.UI.Common.Icons;

namespace Mokaterm.UI.Settings.Pages.Plugins;

/// <summary>
/// How the Plugins page and the install dialog put a plugin into words and colors, so the list, the dialog and the
/// prompts never describe the same thing two ways.
/// </summary>
internal static class PluginDisplay
{
	/// <summary>The warning every step that trusts code shows: installing it turned on, turning it on, trusting new files.</summary>
	public const string CodeWarning =
		"A plugin that runs code has the same access as Mokaterm, including the vault while it is unlocked and every open "
		+ "session. Only turn one on if you trust whoever made it.";

	/// <summary>What the gallery's verified badge means.</summary>
	public const string VerifiedTitle = "Built from source in the gallery's own repository and reviewed by the Mokaterm team.";

	public static string Kind(bool hasCode) => hasCode ? "CODE" : "DATA";

	/// <summary>Code gets the accent because it is what the trust step is about; data runs nothing.</summary>
	public static MokaColor KindColor(bool hasCode) => hasCode ? MokaColor.Primary : MokaColor.Surface;

	public static string KindTitle(bool hasCode) => hasCode
		? "Carries an assembly that runs inside Mokaterm."
		: "Carries data only, such as terminal themes. Nothing in it runs.";

	public static string StatusLabel(PluginStatus status) => status switch
	{
		PluginStatus.Enabled => "On",
		PluginStatus.Disabled => "Off",
		PluginStatus.Failed => "Failed",
		PluginStatus.Changed => "Files changed",
		PluginStatus.Incompatible => "Not compatible",
		PluginStatus.Skipped => "Skipped",
		_ => status.ToString(),
	};

	public static MokaColor StatusColor(PluginStatus status) => status switch
	{
		PluginStatus.Enabled => MokaColor.Success,
		PluginStatus.Failed => MokaColor.Error,
		PluginStatus.Changed or PluginStatus.Incompatible => MokaColor.Warning,
		PluginStatus.Skipped => MokaColor.Info,
		_ => MokaColor.Surface,
	};

	/// <summary>A plugin that is off is the one state with nothing to draw the eye to.</summary>
	public static bool StatusGlows(PluginStatus status) => status != PluginStatus.Disabled;

	public static string StatusTitle(PluginStatus status) => status switch
	{
		PluginStatus.Enabled => "Loaded in this run of Mokaterm.",
		PluginStatus.Disabled => "Installed and turned off.",
		PluginStatus.Failed => "Turned on, but it could not be loaded.",
		PluginStatus.Changed => "Turned on, but its files changed since you trusted them, so it was not loaded.",
		PluginStatus.Incompatible => "Made for another version of Mokaterm or another kind of host.",
		PluginStatus.Skipped => "Turned on, but this start loaded no plugins.",
		_ => "",
	};

	/// <summary>A problem the plugin cannot run with is an error; one the user can settle (changed files) is a warning.</summary>
	public static string ProblemColor(PluginStatus status) => status == PluginStatus.Failed
		? "var(--moka-color-error)"
		: "var(--moka-color-warning)";

	/// <summary>What happens at the next start, or null when nothing is waiting.</summary>
	public static string? PendingText(PluginRecord plugin)
	{
		ArgumentNullException.ThrowIfNull(plugin);
		return plugin.Pending switch
		{
			PluginPendingChange.Install => plugin.IsEnabled
				? "Installed. It loads at the next start."
				: "Installed turned off. It loads once it is turned on.",
			PluginPendingChange.Update => "Updated. The new version loads at the next start.",
			PluginPendingChange.Remove => "Removed at the next start.",
			PluginPendingChange.Enable => "Turns on at the next start.",
			PluginPendingChange.Disable => "Turns off at the next start.",
			_ => null,
		};
	}

	/// <summary>The hosts a plugin names, in words. An empty list means every host.</summary>
	public static IReadOnlyList<string> HostNames(IReadOnlyList<string> hosts)
	{
		ArgumentNullException.ThrowIfNull(hosts);
		return hosts.Count == 0
			? [HostName(PluginHosts.Desktop), HostName(PluginHosts.Web)]
			: [.. hosts.Where(host => !string.IsNullOrWhiteSpace(host)).Select(HostName)];
	}

	public static MokaIconDefinition? HostIcon(string name) => name switch
	{
		"Desktop" => MokatermIcons.Monitor,
		"Web" => MokatermIcons.Globe,
		_ => default(MokaIconDefinition?),
	};

	public static string Themes(int count) => count == 1
		? "1 theme"
		: string.Create(CultureInfo.CurrentCulture, $"{count} themes");

	public static string Publisher(string? publisher) =>
		string.IsNullOrWhiteSpace(publisher) ? "No publisher named" : publisher;

	/// <summary>"Updates 1.0.0 to 1.1.0", or null when nothing with this id is installed.</summary>
	public static string? VersionChange(string? installed, string version)
	{
		if (string.IsNullOrWhiteSpace(installed))
		{
			return null;
		}

		if (string.Equals(installed, version, StringComparison.Ordinal))
		{
			return $"Version {version} is already installed. Installing replaces its files.";
		}

		return PluginVersions.Compare(version, installed) < 0
			? $"Goes back from {installed} to {version}."
			: $"Updates {installed} to {version}.";
	}

	/// <summary>The line the install dialog shows for a package from the gallery.</summary>
	public static string Origin(PluginPackageOrigin origin)
	{
		ArgumentNullException.ThrowIfNull(origin);
		return $"From the Mokaterm gallery, built from {origin.Source} at {ShortCommit(origin.Commit)}";
	}

	/// <summary>A commit as git shortens it, for text; the full one goes in a title.</summary>
	public static string ShortCommit(string commit) => commit.Length > 7 ? commit[..7] : commit;

	/// <summary>A gallery category in words. One the gallery added after this build reads as its own name.</summary>
	public static string Category(string id) => id switch
	{
		"protocols" => "Protocols",
		"server-tools" => "Server tools",
		"docker" => "Docker",
		"utilities" => "Utilities",
		"security" => "Security",
		"themes" => "Themes",
		_ => id.Length == 0 ? id : char.ToUpperInvariant(id[0]) + id[1..].Replace('-', ' '),
	};

	/// <summary>The categories an index uses, the known ones in a fixed order and any newer ones after them.</summary>
	public static IReadOnlyList<string> Categories(IEnumerable<PluginGalleryEntry> plugins)
	{
		ArgumentNullException.ThrowIfNull(plugins);
		HashSet<string> used = new(plugins.SelectMany(plugin => plugin.Categories), StringComparer.Ordinal);
		string[] known = ["protocols", "server-tools", "docker", "utilities", "security", "themes"];
		return [.. known.Where(used.Contains), .. used.Except(known, StringComparer.Ordinal).Order(StringComparer.Ordinal)];
	}

	/// <summary>The warning an installed version the gallery withdrew carries, with the gallery's reason.</summary>
	public static string Withdrawn(PluginGalleryVersion version)
	{
		ArgumentNullException.ThrowIfNull(version);
		return $"Version {version.Version} was withdrawn from the gallery. {version.Withdrawn}";
	}

	/// <summary>
	/// The monospace box under a trust question about an installed plugin: who it is, every file with its size and
	/// SHA-256, and the hash that turning it on trusts, so the list asks the same thing the install dialog shows.
	/// </summary>
	public static string TrustDetails(PluginRecord plugin, PluginFileSet files)
	{
		ArgumentNullException.ThrowIfNull(plugin);
		ArgumentNullException.ThrowIfNull(files);
		System.Text.StringBuilder details = new(Identity(plugin.Id, plugin.Version, plugin.Publisher));
		details.Append("\n\n");
		foreach (PluginPackageFile file in files.Files)
		{
			details.Append(CultureInfo.InvariantCulture, $"{file.Sha256}  {file.Length,10}  {file.Path}\n");
		}

		return details.Append(CultureInfo.InvariantCulture, $"\nPlugin hash: {files.Hash}").ToString();
	}

	/// <summary>The id, version and publisher, for the monospace box under a trust question.</summary>
	public static string Identity(string id, string version, string? publisher) =>
		$"{id} {version}\nPublisher: {(string.IsNullOrWhiteSpace(publisher) ? "not named" : publisher)}";

	private static string HostName(string host) =>
		string.Equals(host, PluginHosts.Desktop, StringComparison.OrdinalIgnoreCase) ? "Desktop"
		: string.Equals(host, PluginHosts.Web, StringComparison.OrdinalIgnoreCase) ? "Web"
		: host.Trim();
}
