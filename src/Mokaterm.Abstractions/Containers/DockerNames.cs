using System.Text.RegularExpressions;

namespace Mokaterm.Abstractions.Containers;

/// <summary>
/// Docker's own rules for every name this app puts on a docker command line: containers, images, volumes, networks,
/// compose projects and services, and the paths compose runs from. Every word is quoted on the way out as well, but a
/// name that breaks the rule is refused before that: a leading dash would read as an option, and a name the daemon
/// would refuse is better refused here with a sentence than there with its error.
/// </summary>
/// <remarks>
/// Each method returns why the value cannot be used, or null when it can. The patterns are Docker's: the container
/// name rule from the daemon, the image reference grammar from the distribution library, the project and service
/// rules from the compose specification.
/// </remarks>
public static partial class DockerNames
{
	/// <summary>The longest name accepted. Docker sets no limit of its own; a reference this long is not a typo but noise.</summary>
	public const int MaxNameLength = 255;

	/// <summary>The longest path compose is told to run from.</summary>
	public const int MaxPathLength = 4096;

	/// <summary>The longest image reference: a 255 character name, a 128 character tag and a long digest.</summary>
	private const int MaxReferenceLength = 512;

	/// <summary>
	/// A container's name or id. The daemon names containers by this rule, and a full or short id (hex) matches it too.
	/// </summary>
	public static string? ContainerProblem(string? reference) =>
		Check(reference, "container", RestrictedName(), "A container is named with letters, digits, '_', '.' and '-', starting with a letter or a digit.");

	/// <summary>
	/// A name for something new: a container, a volume or a network. Docker's restricted name rule, which the daemon
	/// applies to containers and the local volume driver to volumes.
	/// </summary>
	/// <param name="kind">What is being named, for the message: <c>volume</c>, <c>network</c>.</param>
	public static string? NewNameProblem(string? name, string kind)
	{
		ArgumentNullException.ThrowIfNull(kind);
		return Check(
			name,
			kind,
			RestrictedName(),
			$"A {kind} name has at least two characters: letters, digits, '_', '.' and '-', starting with a letter or a digit.");
	}

	/// <summary>
	/// An existing volume or network as a listing printed it. Built-in networks such as <c>host</c> are short, and
	/// plugin drivers name volumes their own way, so this only refuses what a command line cannot carry safely.
	/// </summary>
	public static string? ExistingNameProblem(string? name, string kind)
	{
		ArgumentNullException.ThrowIfNull(kind);
		return Check(name, kind, ExistingName(), $"The {kind} name holds characters this app does not pass to docker.");
	}

	/// <summary>
	/// An image reference: <c>nginx</c>, <c>nginx:1.27</c>, <c>registry:5000/team/app@sha256:...</c>, or an image id.
	/// </summary>
	public static string? ImageProblem(string? reference)
	{
		if (BasicProblem(reference, "image") is { } problem)
		{
			return problem;
		}

		string value = reference!;
		if (ImageId().IsMatch(value))
		{
			return null;
		}

		if (!ImageReference().IsMatch(value))
		{
			return "That is not an image reference. It looks like nginx, nginx:1.27 or registry.example.com/team/app:tag, in lower case.";
		}

		// The name part (before a tag or digest) is capped by the registry protocol, not just by this app.
		int digest = value.IndexOf('@', StringComparison.Ordinal);
		string name = digest < 0 ? value : value[..digest];
		int slash = name.LastIndexOf('/');
		int colon = name.LastIndexOf(':');
		if (colon > slash)
		{
			name = name[..colon];
		}

		return name.Length > MaxNameLength ? "The image name is longer than 255 characters." : null;
	}

	/// <summary>A compose project: lower-case letters, digits, '-' and '_', starting with a letter or a digit.</summary>
	public static string? ProjectProblem(string? name) =>
		Check(name, "project", ProjectName(), "A compose project is named in lower-case letters, digits, '-' and '_', starting with a letter or a digit.");

	/// <summary>A service in a compose file: letters, digits, '.', '-' and '_', starting with a letter or a digit.</summary>
	public static string? ServiceProblem(string? name) =>
		Check(name, "service", ServiceName(), "A compose service is named with letters, digits, '.', '-' and '_', starting with a letter or a digit.");

	/// <summary>
	/// A path on the docker host that compose runs from or reads a file at. It has to be absolute: a relative one would
	/// be relative to wherever the login shell starts. A control character is refused because nothing on a command line
	/// should carry one.
	/// </summary>
	/// <param name="what">What the path is, for the message: <c>working directory</c>, <c>compose file</c>.</param>
	public static string? PathProblem(string? path, string what)
	{
		ArgumentNullException.ThrowIfNull(what);
		if (string.IsNullOrEmpty(path))
		{
			return $"The {what} is missing.";
		}

		if (path.Length > MaxPathLength)
		{
			return $"The {what} is longer than {MaxPathLength} characters.";
		}

		if (path.Any(char.IsControl))
		{
			return $"The {what} holds a control character.";
		}

		return path.StartsWith('/') ? null : $"The {what} {path} is not an absolute path.";
	}

	/// <summary>A full or short container or image id: 12 to 64 hex digits, with or without <c>sha256:</c>.</summary>
	public static bool IsId(string? value) => value is not null && ImageId().IsMatch(value);

	private static string? Check(string? value, string kind, Regex rule, string explanation)
	{
		if (BasicProblem(value, kind) is { } problem)
		{
			return problem;
		}

		return rule.IsMatch(value!) ? null : explanation;
	}

	private static string? BasicProblem(string? value, string kind)
	{
		string trimmed = value?.Trim() ?? "";
		if (trimmed.Length == 0)
		{
			return $"Name a {kind}.";
		}

		// Past this nothing is a real reference, and the patterns never have to walk it.
		if (trimmed.Length > MaxReferenceLength)
		{
			return $"The {kind} name is too long.";
		}

		return trimmed.StartsWith('-') ? $"A {kind} name cannot start with '-', which docker would read as an option." : null;
	}

	[GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9_.-]{1,254}\z", RegexOptions.CultureInvariant)]
	private static partial Regex RestrictedName();

	[GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9_.:@+-]{0,254}\z", RegexOptions.CultureInvariant)]
	private static partial Regex ExistingName();

	[GeneratedRegex(@"^(sha256:)?[a-f0-9]{12,64}\z", RegexOptions.CultureInvariant)]
	private static partial Regex ImageId();

	// distribution/reference: [domain[:port]/]path[:tag][@digest], where the domain may be an IPv6 address in brackets.
	[GeneratedRegex(
		@"^(?:(?:[a-zA-Z0-9]|[a-zA-Z0-9][a-zA-Z0-9-]*[a-zA-Z0-9])(?:\.(?:[a-zA-Z0-9]|[a-zA-Z0-9][a-zA-Z0-9-]*[a-zA-Z0-9]))*(?::[0-9]+)?/|\[[a-fA-F0-9:]+\](?::[0-9]+)?/)?"
		+ @"[a-z0-9]+(?:(?:[._]|__|-+)[a-z0-9]+)*(?:/[a-z0-9]+(?:(?:[._]|__|-+)[a-z0-9]+)*)*"
		+ @"(?::[A-Za-z0-9_][A-Za-z0-9_.-]{0,127})?"
		+ @"(?:@[A-Za-z][A-Za-z0-9]*(?:[-_+.][A-Za-z][A-Za-z0-9]*)*:[0-9a-fA-F]{32,})?\z",
		RegexOptions.CultureInvariant)]
	private static partial Regex ImageReference();

	[GeneratedRegex(@"^[a-z0-9][a-z0-9_-]{0,254}\z", RegexOptions.CultureInvariant)]
	private static partial Regex ProjectName();

	[GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9._-]{0,254}\z", RegexOptions.CultureInvariant)]
	private static partial Regex ServiceName();
}
