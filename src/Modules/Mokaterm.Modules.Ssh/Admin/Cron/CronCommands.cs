using System.Security.Cryptography;
using System.Text;

namespace Mokaterm.Modules.Ssh.Admin.Cron;

/// <summary>
/// Reading and writing a crontab with <c>crontab -l</c> and <c>crontab -</c>, never through an editor, plus the
/// read-only system files and systemd timers.
/// </summary>
internal static class CronCommands
{
	/// <summary>The login's own crontab, or root's when <paramref name="asRoot"/>.</summary>
	public static AdminScript Read(bool asRoot) => new() { Name = "cron.read", Body = "crontab -l", AsRoot = asRoot };

	/// <summary>
	/// The crontab text, or null with <paramref name="problem"/> set. Having no crontab is an empty one: Vixie and cronie
	/// say "no crontab for", BusyBox says it cannot open the file.
	/// </summary>
	public static string? ReadResult(AdminOutcome outcome, out string? problem)
	{
		ArgumentNullException.ThrowIfNull(outcome);
		problem = null;
		if (outcome.Succeeded)
		{
			return outcome.Output;
		}

		string error = outcome.Error;
		if (outcome.ExitCode == 1 && (error.Contains("no crontab for", StringComparison.OrdinalIgnoreCase)
			|| error.Contains("can't open", StringComparison.OrdinalIgnoreCase)
			|| error.Contains("No such file", StringComparison.OrdinalIgnoreCase)))
		{
			return "";
		}

		problem = outcome.Problem();
		return null;
	}

	/// <summary>
	/// Replaces the crontab with <paramref name="document"/>. The new text goes in on stdin. The script first checks
	/// the crontab still hashes to what was loaded, so an edit made elsewhere in the meantime is not overwritten.
	/// </summary>
	public static (AdminAction Action, string Input) Write(CrontabDocument document, string loaded, bool asRoot, string owner, string title)
	{
		ArgumentNullException.ThrowIfNull(document);
		ArgumentNullException.ThrowIfNull(loaded);
		string text = document.ToText();
		string body = string.Join('\n',
			"if command -v sha256sum >/dev/null 2>&1; then",
			"\tcurrent=$(crontab -l 2>/dev/null | sha256sum | cut -d ' ' -f 1)",
			$"\tif [ \"$current\" != '{Sha256(loaded)}' ]; then",
			"\t\techo 'The crontab changed on the server since it was loaded. Reload it and make the change again.' >&2",
			"\t\texit 3",
			"\tfi",
			"fi",
			"crontab -");

		List<string> problems = [.. document.Problems.Select(problem => $"line {problem.Number}")];
		return (new AdminAction
		{
			Title = title,
			Script = new AdminScript
			{
				Name = "cron.write",
				Body = body,
				AsRoot = asRoot,
				ReadsInput = true,
				Timeout = AdminScript.ActionTimeout,
				OutputLimitBytes = AdminScript.ActionOutputBytes,
			},
			Description = $"Installs the crontab below as {owner}'s, which crontab reads from stdin.",
			Warning = problems.Count == 0 ? null : $"cron may refuse the whole file over {string.Join(", ", problems)}, which this view cannot read as an entry.",
			ConfirmText = "Save crontab",
		}, text);
	}

	/// <summary><c>/etc/crontab</c>, each file in <c>/etc/cron.d</c>, and the scripts run hourly, daily, weekly and monthly.</summary>
	public static AdminScript ReadSystem(bool asRoot) => new()
	{
		Name = "cron.system",
		AsRoot = asRoot,
		Body = string.Join('\n',
			"for f in /etc/crontab /etc/cron.d/*; do",
			"\tif [ -f \"$f\" ]; then",
			$"\t\tif [ -r \"$f\" ]; then printf '{AdminSections.Prefix}file %s\\n' \"$f\"; cat -- \"$f\"; echo; else printf '{AdminSections.Prefix}unreadable %s\\n' \"$f\"; fi",
			"\tfi",
			"done",
			"for d in hourly daily weekly monthly; do",
			$"\tif [ -d \"/etc/cron.$d\" ]; then printf '{AdminSections.Prefix}folder %s\\n' \"$d\"; ls -1 \"/etc/cron.$d\" 2>/dev/null; fi",
			"done",
			"exit 0"),
	};

	public static IReadOnlyList<SystemCronFile> ParseSystemFiles(string output)
	{
		AdminSections sections = AdminSections.Parse(output);
		List<SystemCronFile> files = [];
		foreach ((string name, string text) in sections.Parts)
		{
			if (name.StartsWith("file ", StringComparison.Ordinal))
			{
				files.Add(new SystemCronFile(name[5..], CrontabDocument.Parse(text, hasUserField: true), Readable: true));
			}
			else if (name.StartsWith("unreadable ", StringComparison.Ordinal))
			{
				files.Add(new SystemCronFile(name[11..], null, Readable: false));
			}
		}

		return files;
	}

	/// <summary>The scripts in <c>/etc/cron.hourly</c> and the others, by period.</summary>
	public static IReadOnlyList<(string Period, IReadOnlyList<string> Scripts)> ParseSystemFolders(string output) =>
		[
			.. AdminSections.Parse(output).All("folder")
				.Select(folder => (folder.Value, (IReadOnlyList<string>)[.. AdminSections.Lines(folder.Text).Where(name => !name.StartsWith('.'))])),
		];

	/// <summary>Every timer unit, loaded or not running, with what <c>systemctl show</c> knows about its schedule.</summary>
	public static AdminScript ListTimers() => new()
	{
		Name = "cron.timers",
		Body = string.Join('\n',
			"units=$(systemctl list-units --type=timer --all --no-legend --no-pager --plain 2>/dev/null | awk '{print $1}')",
			"if [ -n \"$units\" ]; then",

			// Unit names hold no blanks or glob characters, so the list splits safely unquoted.
			"\tsystemctl show --no-pager -p Id -p Description -p ActiveState -p NextElapseUSecRealtime -p LastTriggerUSec -p Unit -p TimersCalendar -p TimersMonotonic -- $units",
			"fi",
			"exit 0"),
	};

	/// <summary><c>systemctl show</c> blocks of <c>Key=Value</c> lines, one block per unit, separated by blank lines.</summary>
	public static IReadOnlyList<SystemTimer> ParseTimers(string output)
	{
		ArgumentNullException.ThrowIfNull(output);
		List<SystemTimer> timers = [];
		Dictionary<string, string> block = new(StringComparer.Ordinal);
		foreach (string raw in (output + "\n\n").Split('\n'))
		{
			string line = raw.TrimEnd('\r');
			if (line.Length == 0)
			{
				if (block.TryGetValue("Id", out string? id) && id.Length > 0)
				{
					timers.Add(new SystemTimer
					{
						Name = id,
						Description = Value(block, "Description"),
						ActiveState = Value(block, "ActiveState"),
						Next = Time(Value(block, "NextElapseUSecRealtime")),
						Last = Time(Value(block, "LastTriggerUSec")),
						Activates = Value(block, "Unit"),
						Schedule = Schedule(Value(block, "TimersCalendar"), Value(block, "TimersMonotonic")),
					});
				}

				block.Clear();
				continue;
			}

			int equals = line.IndexOf('=', StringComparison.Ordinal);
			if (equals > 0)
			{
				block[line[..equals]] = line[(equals + 1)..];
			}
		}

		return [.. timers.OrderBy(timer => timer.Name, StringComparer.OrdinalIgnoreCase)];
	}

	/// <summary>Lower-case hex SHA-256 of the text as UTF-8, the form <c>sha256sum</c> prints.</summary>
	internal static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

	private static string Value(Dictionary<string, string> block, string key) => block.TryGetValue(key, out string? value) ? value.Trim() : "";

	private static string Time(string value) => value is "n/a" or "0" ? "" : value;

	// "{ OnCalendar=*-*-* 06:00:00 ; next_elapse=... }" and "{ OnBootUSec=15min ; next_elapse=... }", one per trigger.
	private static string Schedule(string calendar, string monotonic)
	{
		List<string> triggers = [];
		foreach (string group in (calendar + " " + monotonic).Split('}', StringSplitOptions.RemoveEmptyEntries))
		{
			string text = group.Trim().TrimStart('{').Trim();
			int separator = text.IndexOf(" ;", StringComparison.Ordinal);
			string trigger = separator < 0 ? text : text[..separator];
			if (trigger.Length > 0)
			{
				triggers.Add(trigger.Replace("USec=", "Sec=", StringComparison.Ordinal));
			}
		}

		return string.Join("; ", triggers);
	}
}
