using System.Globalization;
using Mokaterm.Modules.Ssh.Admin.Monitoring;

namespace Mokaterm.Modules.Ssh.Admin.Processes;

/// <summary>Listing processes with ps and /proc, changing their priority with renice and ending one with kill.</summary>
internal static class ProcessCommands
{
	/// <summary>The nice value that runs first.</summary>
	public const int MinNice = -20;

	/// <summary>The nice value that runs last.</summary>
	public const int MaxNice = 19;

	/// <summary>How often the list reads itself again while auto-refresh is on: each read takes a second of sampling.</summary>
	public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(5);

	/// <summary>
	/// Two readings of every <c>/proc/[pid]/stat</c> a second apart, each after the <c>cpu</c> line of
	/// <c>/proc/stat</c>, so each process's CPU share is measured over that second rather than taken from ps, whose
	/// figure is an average over the process's whole life. awk cuts each line down to five numbers where it exists
	/// (BusyBox has one); without it the whole lines come back. Then procps ps with its header turned off column by
	/// column; BusyBox's ps refuses those columns, so its own smaller set follows with its header, plus the memory
	/// total to work out shares from.
	/// </summary>
	public static AdminScript List(bool asRoot) => new()
	{
		Name = "processes.list",
		AsRoot = asRoot,
		Body = string.Join('\n',
			"mt_ticks() {",
			"\thead -n 1 /proc/stat 2>/dev/null",
			"\tif command -v awk >/dev/null 2>&1; then",
			"\t\tcat /proc/[0-9]*/stat 2>/dev/null | awk '{ p = $1; sub(/^.*[)] /, \"\"); print p, $2, $17, $12, $13 }'",
			"\telse",
			"\t\tcat /proc/[0-9]*/stat 2>/dev/null",
			"\tfi",
			"}",
			AdminSections.Start("memtotal"),
			"grep '^MemTotal:' /proc/meminfo 2>/dev/null",
			AdminSections.Start("cpus"),
			"grep -c '^cpu[0-9]' /proc/stat 2>/dev/null",
			AdminSections.Start("before"),
			"mt_ticks",
			"sleep 1",
			AdminSections.Start("after"),
			"mt_ticks",
			AdminSections.Start("procps"),
			"if ps -eo pid=,user:32=,pcpu=,pmem=,rss=,stat=,args= 2>/dev/null; then exit 0; fi",
			AdminSections.Start("busybox"),
			"ps -o pid,user,rss,stat,args 2>/dev/null || ps",
			"exit 0"),
	};

	/// <summary>The ps listing with each process's sampled CPU share, parent and nice value joined in by PID.</summary>
	public static IReadOnlyList<ProcessInfo> Parse(string output)
	{
		AdminSections sections = AdminSections.Parse(output);
		string? procps = sections.Get("procps");
		IReadOnlyList<ProcessInfo> listed = procps is not null && AdminSections.Lines(procps).Any()
			? ParseProcps(procps)
			: ParseBusyBox(sections.Get("busybox"), MemoryInfo.Parse(sections.Get("memtotal")).TotalKb);

		int cpus = int.TryParse(AdminSections.Lines(sections.Get("cpus")).FirstOrDefault()?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int count) && count > 0 ? count : 1;
		(CpuTimes? before, IReadOnlyDictionary<int, ProcessTicks> first) = ParseTicks(sections.Get("before"));
		(CpuTimes? after, IReadOnlyDictionary<int, ProcessTicks> second) = ParseTicks(sections.Get("after"));
		IReadOnlyDictionary<int, double> shares = CpuShares(before, first, after, second, cpus);
		return [.. listed.Select(process => Join(process, second.TryGetValue(process.Pid, out ProcessTicks ticks) ? ticks : null, shares))];
	}

	/// <summary>A sampling section: the <c>cpu</c> line of /proc/stat first, then one line per process.</summary>
	public static (CpuTimes? Cpu, IReadOnlyDictionary<int, ProcessTicks> Processes) ParseTicks(string? text)
	{
		CpuTimes? cpu = null;
		Dictionary<int, ProcessTicks> processes = [];
		foreach (string line in AdminSections.Lines(text))
		{
			if (cpu is null && CpuTimes.Parse(line) is { } times)
			{
				cpu = times;
			}
			else if (ProcessTicks.Parse(line) is { } ticks)
			{
				processes[ticks.Pid] = ticks;
			}
		}

		return (cpu, processes);
	}

	/// <summary>
	/// Each process's share of one CPU between two readings, in percent, the way top shows it: a process busy on two
	/// cores reads 200. The time base is what every CPU together spent in that interval divided by the CPU count, so
	/// neither the clock tick rate nor the round trip matters. A process that started in between counts all it used.
	/// </summary>
	public static IReadOnlyDictionary<int, double> CpuShares(
		CpuTimes? before,
		IReadOnlyDictionary<int, ProcessTicks> first,
		CpuTimes? after,
		IReadOnlyDictionary<int, ProcessTicks> second,
		int cpus)
	{
		ArgumentNullException.ThrowIfNull(first);
		ArgumentNullException.ThrowIfNull(second);
		Dictionary<int, double> shares = [];
		if (before is not { } start || after is not { } end || second.Count == 0)
		{
			return shares;
		}

		double perCpu = (end.Total - start.Total) / (double)Math.Max(1, cpus);
		if (perCpu <= 0)
		{
			return shares;
		}

		foreach (ProcessTicks ticks in second.Values)
		{
			long used = first.TryGetValue(ticks.Pid, out ProcessTicks earlier) ? ticks.Ticks - earlier.Ticks : ticks.Ticks;
			shares[ticks.Pid] = Math.Round(Math.Max(0, used) * 100 / perCpu, 1);
		}

		return shares;
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
	/// Sets a process's nice value. Raising it is open to the process's owner, so only another user's process needs
	/// root; lowering it (moving the process forward) always needs root. <c>renice N -p PID</c> sets the value itself
	/// with util-linux and BusyBox alike, where <c>-n</c> would mean an absolute value to one and a step to the other.
	/// </summary>
	public static AdminAction Renice(ProcessInfo process, int nice, bool asRoot)
	{
		ArgumentNullException.ThrowIfNull(process);
		if (process.Pid <= 0)
		{
			throw new ArgumentException("The kernel's own idle task has no priority to change.", nameof(process));
		}

		ArgumentOutOfRangeException.ThrowIfLessThan(nice, MinNice);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(nice, MaxNice);
		string pid = process.Pid.ToString(CultureInfo.InvariantCulture);
		string value = nice.ToString(CultureInfo.InvariantCulture);
		return new AdminAction
		{
			Title = $"Renice {process.Name} ({pid}) to {value}",
			Script = new AdminScript
			{
				Name = "processes.renice",
				Body = $"renice {value} -p {pid}",
				AsRoot = asRoot,
				Timeout = AdminScript.ActionTimeout,
				OutputLimitBytes = AdminScript.ActionOutputBytes,
			},
			Description = process.Nice is { } current && nice < current
				? "Moves the process forward: it gets the CPU before processes with a higher nice value. Only root can do this."
				: "Moves the process back: it gets the CPU after processes with a lower nice value, so it slows down only while the machine is busy.",
			Warning = nice < 0 ? "A process this far forward can take the CPU from everything else on the machine, sshd included." : null,
			ConfirmText = "Renice",
		};
	}

	/// <summary>
	/// Whether renicing <paramref name="process"/> to <paramref name="nice"/> needs root for the login
	/// <paramref name="loginName"/>: another user's process, or a lower value than it has now (or one not known).
	/// </summary>
	public static bool ReniceNeedsRoot(ProcessInfo process, int nice, string loginName)
	{
		ArgumentNullException.ThrowIfNull(process);
		return process.User != loginName || process.Nice is not { } current || nice < current;
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

	private static ProcessInfo Join(ProcessInfo process, ProcessTicks? ticks, IReadOnlyDictionary<int, double> shares)
	{
		bool sampled = shares.TryGetValue(process.Pid, out double share);
		return process with
		{
			ParentPid = ticks?.ParentPid,
			Nice = ticks?.Nice,
			Cpu = sampled ? share : process.Cpu,
			CpuSampled = sampled,
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
