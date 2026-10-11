using System.Globalization;

namespace Mokaterm.Modules.Ssh.Admin.Processes;

/// <summary>Listing processes with ps and ending one with kill.</summary>
internal static class ProcessCommands
{
	/// <summary>
	/// procps first, with the header turned off column by column; BusyBox's ps refuses those columns, so its own
	/// smaller set follows with its header, plus the memory total to work out shares from.
	/// </summary>
	public static AdminScript List(bool asRoot) => new()
	{
		Name = "processes.list",
		AsRoot = asRoot,
		Body = string.Join('\n',
			AdminSections.Start("memtotal"),
			"grep '^MemTotal:' /proc/meminfo 2>/dev/null",
			AdminSections.Start("procps"),
			"if ps -eo pid=,user:32=,pcpu=,pmem=,rss=,stat=,args= 2>/dev/null; then exit 0; fi",
			AdminSections.Start("busybox"),
			"ps -o pid,user,rss,stat,args 2>/dev/null || ps",
			"exit 0"),
	};

	public static IReadOnlyList<ProcessInfo> Parse(string output)
	{
		AdminSections sections = AdminSections.Parse(output);
		string? procps = sections.Get("procps");
		if (procps is not null && AdminSections.Lines(procps).Any())
		{
			return ParseProcps(procps);
		}

		long? memTotal = null;
		string[] total = (AdminSections.Lines(sections.Get("memtotal")).FirstOrDefault() ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (total.Length >= 2 && long.TryParse(total[1], NumberStyles.None, CultureInfo.InvariantCulture, out long kb) && kb > 0)
		{
			memTotal = kb;
		}

		return ParseBusyBox(sections.Get("busybox"), memTotal);
	}

	/// <summary><c>pid user pcpu pmem rss stat args...</c> without a header.</summary>
	public static IReadOnlyList<ProcessInfo> ParseProcps(string text)
	{
		List<ProcessInfo> processes = [];
		foreach (string line in AdminSections.Lines(text))
		{
			string[] words = line.Trim().Split(' ', 7, StringSplitOptions.RemoveEmptyEntries);
			if (words.Length < 7 || !int.TryParse(words[0], NumberStyles.None, CultureInfo.InvariantCulture, out int pid))
			{
				continue;
			}

			processes.Add(new ProcessInfo
			{
				Pid = pid,
				User = words[1],
				Cpu = Number(words[2]),
				Memory = Number(words[3]),
				ResidentKb = long.TryParse(words[4], NumberStyles.None, CultureInfo.InvariantCulture, out long rss) ? rss : null,
				State = words[5],
				Command = words[6].Trim(),
			});
		}

		return processes;
	}

	/// <summary>
	/// BusyBox <c>ps -o pid,user,rss,stat,args</c> with its header line. Older BusyBox prints sizes such as
	/// <c>12m</c>, which are read as well. With no <c>-o</c> support at all, the default <c>PID USER TIME COMMAND</c>.
	/// </summary>
	public static IReadOnlyList<ProcessInfo> ParseBusyBox(string? text, long? memoryTotalKb)
	{
		List<ProcessInfo> processes = [];
		string[]? header = null;
		foreach (string line in AdminSections.Lines(text))
		{
			string[] words = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
			if (header is null)
			{
				header = words;
				continue;
			}

			int columns = header.Length;
			int pidAt = Index(header, "PID");
			string[] fields = line.Trim().Split(' ', columns, StringSplitOptions.RemoveEmptyEntries);
			if (pidAt < 0 || fields.Length < columns || !int.TryParse(fields[pidAt], NumberStyles.None, CultureInfo.InvariantCulture, out int pid))
			{
				continue;
			}

			long? rss = Index(header, "RSS") is var rssAt and >= 0 ? Size(fields[rssAt]) : null;
			processes.Add(new ProcessInfo
			{
				Pid = pid,
				User = Index(header, "USER") is var userAt and >= 0 ? fields[userAt] : "",
				ResidentKb = rss,
				Memory = rss is { } resident && memoryTotalKb is { } memory ? Math.Round(resident * 100.0 / memory, 1) : null,
				State = Index(header, "STAT") is var statAt and >= 0 ? fields[statAt] : "",
				Command = fields[^1].Trim(),
			});
		}

		return processes;
	}

	/// <summary>
	/// Asks a process to end (TERM) or ends it at once (KILL). PID 1 is refused here: killing init panics the kernel or
	/// takes down the container.
	/// </summary>
	public static AdminAction Signal(ProcessInfo process, bool force, bool asRoot)
	{
		ArgumentNullException.ThrowIfNull(process);
		if (process.Pid <= 1)
		{
			throw new ArgumentException("PID 1 and the kernel's own threads cannot be ended here.", nameof(process));
		}

		string pid = process.Pid.ToString(CultureInfo.InvariantCulture);
		string name = process.Name;
		return new AdminAction
		{
			Title = force ? $"Kill {name} ({pid})" : $"Terminate {name} ({pid})",
			Script = new AdminScript
			{
				Name = force ? "processes.kill" : "processes.term",
				Body = $"kill -s {(force ? "KILL" : "TERM")} {pid}",
				AsRoot = asRoot,
				Timeout = AdminScript.ActionTimeout,
				OutputLimitBytes = AdminScript.ActionOutputBytes,
			},
			Description = force
				? "Ends the process at once. It gets no chance to save anything or clean up."
				: "Asks the process to end. Most programs finish what they are doing and exit; some ignore it.",
			Warning = name is "sshd" or "sshd-session" or "dropbear"
				? "This is an SSH server process, and it may be the one carrying this session."
				: name is "systemd" or "init" or "dbus-daemon" or "dbus-broker" or "systemd-journald" or "systemd-logind"
					? "The system relies on this process; ending it can take services or the whole machine down."
					: null,
			Destructive = true,
			ConfirmText = force ? "Kill" : "Terminate",
		};
	}

	private static int Index(string[] header, string column) => Array.IndexOf(header, column);

	private static double? Number(string text) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : null;

	private static long? Size(string text)
	{
		if (long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long kb))
		{
			return kb;
		}

		if (text.Length > 1 && long.TryParse(text[..^1], NumberStyles.None, CultureInfo.InvariantCulture, out long scaled))
		{
			return char.ToLowerInvariant(text[^1]) switch
			{
				'm' => scaled * 1024,
				'g' => scaled * 1024 * 1024,
				_ => null,
			};
		}

		return null;
	}
}
