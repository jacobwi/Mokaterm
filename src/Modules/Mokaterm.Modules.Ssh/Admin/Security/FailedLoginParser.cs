using System.Globalization;
using System.Text.RegularExpressions;
using Mokaterm.Modules.Ssh.Tools;

namespace Mokaterm.Modules.Ssh.Admin.Security;

/// <summary>
/// Turns sshd's log lines, from the journal or a syslog file, and <c>lastb</c>'s table into failed logins. sshd writes
/// "Invalid user" when a name does not exist and then "Failed password for invalid user" for each password tried, so an
/// "Invalid user" line only counts when no failure from the same address and port follows it.
/// </summary>
internal static partial class FailedLoginParser
{
	/// <summary>Lines of sshd's log, oldest first, as the journal, auth.log or secure keep them.</summary>
	public static IReadOnlyList<FailedLogin> ParseLog(string? text)
	{
		// Kept in the log's own order: a syslog time has no year, so it cannot be sorted on.
		List<FailedLogin> entries = [];
		foreach (string line in AdminSections.Lines(text))
		{
			Match failed = FailedPattern().Match(line);
			if (failed.Success)
			{
				entries.Add(new FailedLogin
				{
					Time = TimeOf(line),
					Method = failed.Groups["method"].Value,
					InvalidUser = failed.Groups["invalid"].Success,
					User = failed.Groups["user"].Value,
					Address = failed.Groups["address"].Value,
					Port = Port(failed),
				});
				continue;
			}

			Match invalid = InvalidPattern().Match(line);
			if (invalid.Success)
			{
				entries.Add(new FailedLogin
				{
					Time = TimeOf(line),
					InvalidUser = true,
					User = invalid.Groups["user"].Value,
					Address = invalid.Groups["address"].Value,
					Port = Port(invalid),
				});
			}
		}

		HashSet<(string, int?)> tried = [.. entries.Where(entry => entry.Method.Length > 0).Select(entry => (entry.Address, entry.Port))];
		return [.. entries.Where(entry => entry.Method.Length > 0 || !tried.Contains((entry.Address, entry.Port)))];
	}

	/// <summary>
	/// <c>lastb -w --time-format iso</c>: user, line, host and the attempt's time, newest first. Only attempts over SSH
	/// (<c>ssh:notty</c>) count; the closing "btmp begins" line is not one.
	/// </summary>
	public static IReadOnlyList<FailedLogin> ParseLastb(string? text)
	{
		List<FailedLogin> failures = [];
		foreach (string line in AdminSections.Lines(text))
		{
			string[] words = line.Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
			if (words.Length < 3 || !words[1].StartsWith("ssh:", StringComparison.Ordinal))
			{
				continue;
			}

			int time = Array.FindIndex(words, 2, word => LogLines.IsTimestamp(word));
			if (time < 0)
			{
				continue;
			}

			failures.Add(new FailedLogin
			{
				Time = words[time],
				User = words[0],
				Address = time > 2 ? words[2] : "",
			});
		}

		// lastb lists the newest first; everything else here reads oldest first.
		failures.Reverse();
		return failures;
	}

	/// <summary>The addresses that failed most, with how often and when last, most first. <paramref name="failures"/> is oldest first.</summary>
	public static IReadOnlyList<(string Address, int Count, string Last)> ByAddress(IEnumerable<FailedLogin> failures) =>
		[.. (failures ?? throw new ArgumentNullException(nameof(failures)))
			.Where(failure => failure.Address.Length > 0)
			.GroupBy(failure => failure.Address, StringComparer.Ordinal)
			.Select(group => (group.Key, group.Count(), group.Last().Time))
			.OrderByDescending(entry => entry.Item2)
			.ThenBy(entry => entry.Key, StringComparer.Ordinal)];

	/// <summary>The line's own time: the journal's or rsyslog's ISO stamp, or a classic syslog <c>Mmm dd hh:mm:ss</c>.</summary>
	internal static string TimeOf(string line)
	{
		int space = line.IndexOf(' ', StringComparison.Ordinal);
		if (space > 0 && LogLines.IsTimestamp(line.AsSpan(0, space)))
		{
			return line[..space];
		}

		return SyslogTime().Match(line) is { Success: true } classic ? classic.Value : "";
	}

	private static int? Port(Match match) =>
		match.Groups["port"].Success && int.TryParse(match.Groups["port"].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out int port) ? port : null;

	[GeneratedRegex(@"Failed (?<method>[a-z][a-z/-]*) for (?<invalid>invalid user )?(?<user>\S*) from (?<address>[0-9A-Fa-f.:]+) port (?<port>[0-9]+)", RegexOptions.CultureInvariant)]
	private static partial Regex FailedPattern();

	[GeneratedRegex(@"Invalid user (?<user>\S*) from (?<address>[0-9A-Fa-f.:]+)(?: port (?<port>[0-9]+))?", RegexOptions.CultureInvariant)]
	private static partial Regex InvalidPattern();

	[GeneratedRegex(@"^[A-Z][a-z]{2} [ 0-9][0-9] [0-9]{2}:[0-9]{2}:[0-9]{2}", RegexOptions.CultureInvariant)]
	private static partial Regex SyslogTime();
}
