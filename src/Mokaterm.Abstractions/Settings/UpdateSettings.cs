namespace Mokaterm.Abstractions.Settings;

/// <summary>
/// Where the app looks for new versions. Only a host that can replace its own files reads this, and it holds nothing
/// secret: settings live in the plain settings document.
/// </summary>
public sealed record UpdateSettings : ISettingsSection
{
	public static string SectionKey => "updates";

	/// <summary>The feed rule as a hint for the field.</summary>
	public const string FeedRequirement = "Enter an https address or a full folder path.";

	/// <summary>This project's releases on GitHub, which every release publishes its update files to.</summary>
	public const string GitHubReleases = "https://github.com/jacobwi/Mokaterm";

	/// <summary>
	/// The release feed: a GitHub repository's address (its releases), another http or https address, or a full path to a
	/// local or network folder holding the packages. Empty turns updates off. A new install starts with
	/// <see cref="GitHubReleases"/>; an install that saved its own feed, empty included, keeps it.
	/// </summary>
	[MachineSpecific]
	public string Feed { get; init; } = GitHubReleases;

	/// <summary>Look for a new version once after the app starts. Finding one only shows a notice.</summary>
	public bool CheckAtStartup { get; init; } = true;

	/// <summary>
	/// <paramref name="value"/> trimmed when it is an absolute http or https address or a fully qualified path,
	/// otherwise empty. The one rule for a feed: the settings page refuses what this drops, and a hand-edited
	/// settings.json falls back to updates being off instead of to a location nobody meant.
	/// </summary>
	/// <summary>
	/// True for a GitHub repository's address, <c>https://github.com/owner/repo</c>, which is read through GitHub's
	/// release list rather than as a folder of files: a release only carries its own packages, and the list is how an
	/// update finds the ones before it for a smaller download.
	/// </summary>
	public static bool IsGitHubRepository(string? feed)
	{
		if (!Uri.TryCreate(feed?.Trim(), UriKind.Absolute, out Uri? url)
			|| url.Scheme != Uri.UriSchemeHttps
			|| !url.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
			|| url.Query.Length > 0
			|| url.Fragment.Length > 0)
		{
			return false;
		}

		string[] segments = url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
		return segments.Length == 2;
	}

	public static string NormalizeFeed(string? value)
	{
		string feed = value?.Trim() ?? "";
		if (feed.Length == 0 || feed.Any(char.IsControl))
		{
			return "";
		}

		// A Windows path parses as an absolute file: URI, hence the scheme check before the path one.
		bool address = Uri.TryCreate(feed, UriKind.Absolute, out Uri? url)
			&& (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps)
			&& url.Host.Length > 0;

		return address || Path.IsPathFullyQualified(feed) ? feed : "";
	}
}
