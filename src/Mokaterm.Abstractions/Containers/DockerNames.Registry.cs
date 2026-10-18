using System.Text.RegularExpressions;

namespace Mokaterm.Abstractions.Containers;

/// <summary>
/// What <c>docker login</c>, <c>docker logout</c> and <c>docker build</c> are given beyond image references: a registry, a
/// user name for it, a platform and a build stage. Each method returns why the value cannot be used, or null when it can,
/// like the name rules.
/// </summary>
public static partial class DockerNames
{
	/// <summary>The registry <c>docker login</c> uses when none is named: Docker Hub, as its config file keys it.</summary>
	public const string DockerHubRegistry = "https://index.docker.io/v1/";

	/// <summary>
	/// A registry as <c>docker login</c> takes it and as a client config keys it: a host with an optional port
	/// (<c>ghcr.io</c>, <c>registry.example.com:5000</c>, <c>[::1]:5000</c>), optionally with <c>https://</c> or
	/// <c>http://</c> in front and a path behind, as Docker Hub's own key has.
	/// </summary>
	public static string? RegistryProblem(string? registry)
	{
		if (BasicProblem(registry, "registry") is { } problem)
		{
			return problem;
		}

		string value = registry!.Trim();
		return value.Length <= MaxNameLength && Registry().IsMatch(value)
			? null
			: "A registry is a host name with an optional port, such as ghcr.io or registry.example.com:5000.";
	}

	/// <summary>
	/// A user name for a registry. Registries name their accounts every way there is (an e-mail address,
	/// <c>oauth2accesstoken</c>, <c>_json_key</c>, a GUID), so only what a command line cannot carry as one word is refused.
	/// </summary>
	public static string? RegistryUserProblem(string? user)
	{
		if (string.IsNullOrEmpty(user))
		{
			return "Name the user.";
		}

		if (user.Length > MaxNameLength)
		{
			return "The user name is longer than 255 characters.";
		}

		return user.Any(character => char.IsWhiteSpace(character) || char.IsControl(character))
			? "A user name has no spaces or control characters."
			: null;
	}

	/// <summary>A platform to build for: <c>linux/amd64</c>, <c>linux/arm64/v8</c>. One only, since the image is loaded here.</summary>
	public static string? PlatformProblem(string? platform)
	{
		string text = platform?.Trim() ?? "";
		if (text.Length == 0)
		{
			return "Name a platform, such as linux/amd64.";
		}

		if (text.Contains(',', StringComparison.Ordinal))
		{
			return "Build for one platform at a time: an image for several cannot be loaded into this host's images.";
		}

		return text.Length <= 64 && Platform().IsMatch(text)
			? null
			: "A platform is os/arch[/variant] in lower case, such as linux/amd64 or linux/arm64/v8.";
	}

	/// <summary>A build stage of a multi-stage Dockerfile, as <c>FROM image AS name</c> names it.</summary>
	public static string? StageProblem(string? stage)
	{
		string text = stage?.Trim() ?? "";
		if (text.Length == 0)
		{
			return "Name the stage.";
		}

		return Stage().IsMatch(text)
			? null
			: "A stage is named with letters, digits, '_', '.' and '-', starting with a letter.";
	}

	// [scheme://]host[:port][/path], the host a DNS name, an IPv4 address (digits and dots pass the name rule) or [IPv6].
	[GeneratedRegex(
		@"^(?:https?://)?(?:(?:[a-zA-Z0-9]|[a-zA-Z0-9][a-zA-Z0-9-]*[a-zA-Z0-9])(?:\.(?:[a-zA-Z0-9]|[a-zA-Z0-9][a-zA-Z0-9-]*[a-zA-Z0-9]))*|\[[a-fA-F0-9:]+\])"
		+ @"(?::[0-9]{1,5})?(?:/[A-Za-z0-9._~-]*)*\z",
		RegexOptions.CultureInvariant)]
	private static partial Regex Registry();

	[GeneratedRegex(@"^[a-z0-9_]+/[a-z0-9_]+(?:/[a-z0-9_.]+)?\z", RegexOptions.CultureInvariant)]
	private static partial Regex Platform();

	[GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_.-]{0,127}\z", RegexOptions.CultureInvariant)]
	private static partial Regex Stage();
}
