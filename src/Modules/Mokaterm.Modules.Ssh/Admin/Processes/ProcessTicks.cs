using System.Globalization;

namespace Mokaterm.Modules.Ssh.Admin.Processes;

/// <summary>One process's line of <c>/proc/[pid]/stat</c>, down to what the list uses: its parent, its nice value and the CPU it used.</summary>
/// <param name="Ticks">User plus system time since the process started, in clock ticks, all its threads together.</param>
internal readonly record struct ProcessTicks(int Pid, int ParentPid, int Nice, long Ticks)
{
	/// <summary>
	/// Either the four numbers the sampling script's awk prints (<c>pid ppid nice utime stime</c>) or a whole stat
	/// line, which is what arrives when the server has no awk. In a whole line the command name sits in parentheses
	/// and may hold blanks and parentheses itself, so the fields are counted from the last <c>") "</c>.
	/// </summary>
	public static ProcessTicks? Parse(string line)
	{
		ArgumentNullException.ThrowIfNull(line);
		int close = line.LastIndexOf(") ", StringComparison.Ordinal);
		if (close >= 0)
		{
			int open = line.IndexOf(" (", StringComparison.Ordinal);
			if (open <= 0 || open > close)
			{
				return null;
			}

			// After the name: state ppid pgrp session tty tpgid flags minflt cminflt majflt cmajflt utime stime
			// cutime cstime priority nice.
			string[] rest = line[(close + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
			return rest.Length >= 17 ? From(line[..open], rest[1], rest[16], rest[11], rest[12]) : null;
		}

		string[] words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		return words.Length == 5 ? From(words[0], words[1], words[2], words[3], words[4]) : null;
	}

	private static ProcessTicks? From(string pid, string parent, string nice, string user, string system) =>
		int.TryParse(pid.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int id)
		&& int.TryParse(parent, NumberStyles.None, CultureInfo.InvariantCulture, out int parentId)
		&& int.TryParse(nice, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int niceness)
		&& long.TryParse(user, NumberStyles.None, CultureInfo.InvariantCulture, out long userTicks)
		&& long.TryParse(system, NumberStyles.None, CultureInfo.InvariantCulture, out long systemTicks)
			? new ProcessTicks(id, parentId, niceness, userTicks + systemTicks)
			: null;
}
