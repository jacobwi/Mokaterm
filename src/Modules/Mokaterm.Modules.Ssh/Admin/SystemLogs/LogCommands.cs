using System.Globalization;
using Mokaterm.Modules.Ssh.Shell;

namespace Mokaterm.Modules.Ssh.Admin.SystemLogs;

/// <summary>Reading the journal with filters, or the files under <c>/var/log</c> where there is no journal.</summary>
internal static class LogCommands
{
	/// <summary>journalctl's priority names, most severe first. A filter shows its level and everything above it.</summary>
	public static readonly IReadOnlyList<string> Priorities = ["emerg", "alert", "crit", "err", "warning", "notice", "info", "debug"];

	/// <summary>The line counts the page offers; more would not be read in a dialog anyway.</summary>
	public static readonly IReadOnlyList<int> LineCounts = [50, 200, 500, 1000, 2000];

	public const int MaxLines = 2000;

	/// <summary>The journal's last lines, newest last, for one unit and at one priority or above when given.</summary>
	public static AdminScript Journal(string? unit, string? priority, int lines, bool asRoot)
	{
		List<string> arguments = ["journalctl --no-pager -o short-iso", $"-n {Lines(lines)}"];
		if (!string.IsNullOrEmpty(unit))
		{
			if (AdminNames.CheckService(unit) is { } problem)
			{
				throw new ArgumentException(problem, nameof(unit));
			}

			arguments.Add($"-u {PosixShell.Quote(unit)}");
		}

		if (!string.IsNullOrEmpty(priority))
		{
			if (!Priorities.Contains(priority))
			{
				throw new ArgumentException("Pick a priority from the list.", nameof(priority));
			}

			arguments.Add($"-p {priority}");
		}

		// journalctl prints its "you are not seeing everything" hint on stderr; it belongs next to the lines.
		return new AdminScript { Name = "logs.journal", Body = string.Join(' ', arguments) + " 2>&1", AsRoot = asRoot };
	}

	/// <summary>Plain text logs under <c>/var/log</c>; rotated and compressed copies and binary accounting files are left out.</summary>
	public static AdminScript ListFiles(bool asRoot) => new()
	{
		Name = "logs.files",
		AsRoot = asRoot,
		Body = string.Join(' ',
			"find /var/log -maxdepth 3 -type f",
			"! -name '*.gz' ! -name '*.xz' ! -name '*.bz2' ! -name '*.zst' ! -name '*.[0-9]' ! -name '*.old'",
			"! -name wtmp ! -name btmp ! -name lastlog ! -name faillog ! -name '*.journal' ! -name '*.journal~'",
			"2>/dev/null | sort | head -n 500"),
	};

	public static IReadOnlyList<string> ParseFiles(string output) =>
		[.. AdminSections.Lines(output).Select(line => line.Trim()).Where(path => AdminNames.CheckLogPath(path) is null)];

	public static AdminScript ReadFile(string path, int lines, bool asRoot)
	{
		if (AdminNames.CheckLogPath(path) is { } problem)
		{
			throw new ArgumentException(problem, nameof(path));
		}

		return new AdminScript { Name = "logs.file", Body = $"tail -n {Lines(lines)} -- {PosixShell.Quote(path)}", AsRoot = asRoot };
	}

	private static string Lines(int lines) => Math.Clamp(lines, 1, MaxLines).ToString(CultureInfo.InvariantCulture);
}
