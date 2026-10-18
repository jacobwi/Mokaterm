using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.Sessions.Terminal;

/// <summary>
/// The names <see cref="SessionLogWriter"/> gives its files, and the test that tells one apart from anything else. The
/// folder is the user's choice and may hold files of their own, so listing, cleanup and deleting all go by
/// <see cref="IsGenerated"/>: a file this does not recognise is never touched.
/// </summary>
internal static partial class SessionLogNames
{
	/// <summary>Names tried before giving up, for two sessions to the same host inside one second.</summary>
	public const int MaxAttempts = 20;

	private const int HostLength = 48;
	private const int UserLength = 32;
	private const string StampFormat = "yyyyMMdd-HHmmss";

	/// <summary>What the formats write: the text formats, then timed recordings.</summary>
	public static IReadOnlyList<string> Extensions { get; } = [".log", Asciicast.Extension];

	/// <summary>Turns a session into a name a human can find again: <c>web01-root-20260926-142530.log</c>.</summary>
	/// <param name="endpoint">The session as <c>user@host:port</c>.</param>
	/// <param name="attempt">0 for the first try, then 1 up to <see cref="MaxAttempts"/> while the name is taken.</param>
	/// <param name="extension">The format's extension, dot included.</param>
	public static string Build(string endpoint, DateTimeOffset startedAt, int attempt, string extension = ".log")
	{
		string user = "";
		string host = endpoint;
		int at = endpoint.IndexOf('@', StringComparison.Ordinal);
		if (at >= 0)
		{
			user = endpoint[..at];
			host = endpoint[(at + 1)..];
		}

		// The port goes: an IPv6 address is full of colons, and the one that matters is the last.
		int colon = host.LastIndexOf(':');
		if (colon > 0)
		{
			host = host[..colon];
		}

		StringBuilder name = new();
		name.Append(Slug(host, HostLength));
		if (Slug(user, UserLength) is { Length: > 0 } slug)
		{
			name.Append('-').Append(slug);
		}

		name.Append('-').Append(startedAt.ToString(StampFormat, CultureInfo.InvariantCulture));
		if (attempt > 0)
		{
			name.Append(CultureInfo.InvariantCulture, $"-{attempt}");
		}

		return name.Append(extension).ToString();
	}

	/// <summary>
	/// True for a name <see cref="Build"/> can produce: the slugs, a real date and time, an attempt number in range and
	/// one of the formats' extensions, exactly as written. Anything else, a renamed copy or another program's
	/// <c>.log</c> included, is not one of ours.
	/// </summary>
	public static bool IsGenerated(string? name)
	{
		if (string.IsNullOrEmpty(name) || Extensions.FirstOrDefault(extension => name.EndsWith(extension, StringComparison.Ordinal)) is not { } suffix)
		{
			return false;
		}

		Match match = StemPattern().Match(name[..^suffix.Length]);
		if (!match.Success
			|| !DateTime.TryParseExact(match.Groups["stamp"].Value, StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
		{
			return false;
		}

		Group attempt = match.Groups["attempt"];
		return !attempt.Success
			|| (attempt.Value[0] != '0'
				&& int.Parse(attempt.Value, NumberStyles.None, CultureInfo.InvariantCulture) <= MaxAttempts);
	}

	/// <summary>Keeps letters, digits, dots, dashes and underscores; every other run, an IPv6 colon included, becomes one underscore.</summary>
	private static string Slug(string value, int maxLength)
	{
		StringBuilder slug = new(value.Length);
		bool filler = false;
		foreach (char character in value)
		{
			if (slug.Length >= maxLength)
			{
				break;
			}

			if (char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_')
			{
				slug.Append(character);
				filler = false;
			}
			else if (!filler && slug.Length > 0)
			{
				slug.Append('_');
				filler = true;
			}
		}

		return slug.ToString().Trim('_', '.', '-');
	}

	// The host and user slugs (48 and 32 characters, joined by a dash, either one possibly empty), the stamp and an
	// optional attempt. ASCII classes only: a digit class would also take digits from other scripts.
	[GeneratedRegex("^[A-Za-z0-9._-]{0,81}-(?<stamp>[0-9]{8}-[0-9]{6})(?:-(?<attempt>[0-9]{1,2}))?$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
	private static partial Regex StemPattern();
}
