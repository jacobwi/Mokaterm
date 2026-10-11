namespace Mokaterm.Modules.Ssh.Admin.Cron;

/// <summary>One scheduled command. <see cref="User"/> is only there in the system files, which name the account to run as.</summary>
internal sealed record CrontabEntry(CronSchedule Schedule, string Command, string? User = null)
{
	/// <summary>The entry as a crontab line.</summary>
	public string Format() => User is null ? $"{Schedule.Text} {Command}" : $"{Schedule.Text} {User} {Command}";

	/// <summary>Why <paramref name="command"/> cannot go on a crontab line, or null.</summary>
	public static string? CheckCommand(string? command)
	{
		if (string.IsNullOrWhiteSpace(command))
		{
			return "Type the command to run.";
		}

		if (command.Any(character => char.IsControl(character) && character != '\t'))
		{
			return "A crontab command is one line, without control characters.";
		}

		return null;
	}

	/// <summary>
	/// cron turns a <c>%</c> that is not escaped into a line break and feeds the rest to the command's stdin, which
	/// surprises anyone writing <c>date +%F</c>. Null when the command has none.
	/// </summary>
	public static string? PercentNote(string? command)
	{
		if (string.IsNullOrEmpty(command))
		{
			return null;
		}

		for (int index = 0; index < command.Length; index++)
		{
			if (command[index] == '\\')
			{
				index++;
			}
			else if (command[index] == '%')
			{
				return "cron reads % as a line break and sends the rest to the command's input. Write \\% for a percent sign.";
			}
		}

		return null;
	}

	/// <summary>A schedule and a command the editor can save, or the first problem with either.</summary>
	public static CrontabEntry? TryCreate(string schedule, string command, out string? problem)
	{
		if (CronSchedule.TryParse(schedule ?? "", out problem) is not { } parsed)
		{
			return null;
		}

		problem = CheckCommand(command);
		return problem is null ? new CrontabEntry(parsed, command.Trim()) : null;
	}
}
