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

	/// <summary>
	/// The release feed: an http or https address, or a full path to a local or network folder holding the packages.
	/// Empty turns updates off, which is the default, because no public feed ships with the app.
	/// </summary>
	public string Feed { get; init; } = "";

	/// <summary>Look for a new version once after the app starts. Finding one only shows a notice.</summary>
	public bool CheckAtStartup { get; init; } = true;

	/// <summary>
	/// <paramref name="value"/> trimmed when it is an absolute http or https address or a fully qualified path,
	/// otherwise empty. The one rule for a feed: the settings page refuses what this drops, and a hand-edited
	/// settings.json falls back to updates being off instead of to a location nobody meant.
	/// </summary>
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
