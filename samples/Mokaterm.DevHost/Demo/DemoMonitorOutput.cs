using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Mokaterm.DevHost.Demo.FileSystem;

namespace Mokaterm.DevHost.Demo;

/// <summary>
/// The system view's live and on-demand reads on the demo machine: the overview's samples, the process list, disk
/// usage, SMART and followed logs. Every counter is worked out from the clock, so two reads a moment apart differ the
/// way a busy Ubuntu server's would, and a follow finds new lines each time it asks.
/// </summary>
internal static partial class DemoMonitorOutput
{
	/// <summary>When the demo machine booted.</summary>
	private static readonly DateTimeOffset Boot = new(2026, 9, 29, 8, 12, 44, TimeSpan.Zero);

	private const int Cores = 4;

	/// <summary>
	/// The stdout a read prints after its marker, or null for a script this part of the demo does not answer.
	/// <paramref name="command"/> is the command line as sent, <c>sh -c '...' sh</c>, which the values are read from.
	/// </summary>
	public static string? For(string script, string command, DemoAccount account, DateTimeOffset now)
	{
		string body = Unwrap(command);
		return script switch
		{
			"overview.sample" => Sample(now),
			"processes.list" => Processes(account, now),
			"disks.usage" => Usage(body, account),
			"disks.large-files" => LargeFiles(body, account),
			"disks.smart" => Smart(body, now),
			"logs.journal" => Journal(body, now),
			"logs.journal.follow" => FollowJournal(body, now),
			"logs.file" => ReadFile(body, now),
			"logs.file.follow" => FollowFile(body, now),
			_ => null,
		};
	}

	/// <summary>The sections every live sample prints, which the overview's first read prints too.</summary>
	public static string Sample(DateTimeOffset now)
	{
		double t = Seconds(now);
		long total = (long)(t * 100 * Cores);
		long busy = (long)(100 * Cores * Integral(t, 0.30, (0.15, 17), (0.08, 5.3)));
		long ioWait = (long)(100 * Cores * Integral(t, 0.02, (0.015, 11)));
		long idle = total - busy - ioWait;
		long available = 5_122_000 + (long)(300_000 * Math.Sin(t / 45));
		long swapFree = 1_897_148 - (long)(20_000 * Math.Sin(t / 90));
		double load = 0.9 + (0.5 * Math.Sin(t / 40));
		StringBuilder text = new();
		text.Append(CultureInfo.InvariantCulture, $"@@mt:uptime\n{t:0.00} {t * 3.7:0.00}\n")
			.Append(CultureInfo.InvariantCulture, $"@@mt:stat\ncpu  {busy * 70 / 100} {busy / 50} {busy * 23 / 100} {idle} {ioWait} 0 {busy - (busy * 70 / 100) - (busy / 50) - (busy * 23 / 100)} 0 0 0\n")
			.Append(CultureInfo.InvariantCulture, $"@@mt:loadavg\n{load:0.00} {0.6 + (0.2 * Math.Sin(t / 300)):0.00} {0.45:0.00} 2/318 48211\n")
			.Append(CultureInfo.InvariantCulture, $"""
				@@mt:meminfo
				MemTotal:        8037888 kB
				MemFree:          912000 kB
				MemAvailable:    {available} kB
				Buffers:          182044 kB
				Cached:          3402112 kB
				SwapTotal:       2097148 kB
				SwapFree:        {swapFree} kB

				""")
			.Append("@@mt:netdev\nInter-|   Receive                                                |  Transmit\n")
			.Append(" face |bytes    packets errs drop fifo frame compressed multicast|bytes    packets errs drop fifo colls carrier compressed\n")
			.Append(NetLine("lo", 38_211_042 + (long)(800 * t), 38_211_042 + (long)(800 * t)))
			.Append(NetLine("eth0", (long)Integral(t, 180_000, (120_000, 9), (40_000, 3.1)), (long)Integral(t, 60_000, (40_000, 7))))
			.Append(NetLine("docker0", (long)Integral(t, 9_000, (6_000, 13)), (long)Integral(t, 21_000, (12_000, 13))))
			.Append(NetLine("veth3f1a2b9", (long)Integral(t, 9_000, (6_000, 13)), (long)Integral(t, 21_000, (12_000, 13))))
			.Append("@@mt:nics\n/sys/class/net/eth0/device\n")
			.Append("@@mt:diskstats\n")
			.Append(DiskLine(253, 0, "vda", Integral(t, 40_000, (30_000, 8)), Integral(t, 300_000, (200_000, 13), (90_000, 2.7))))
			.Append(DiskLine(253, 1, "vda1", Integral(t, 39_000, (29_000, 8)), Integral(t, 298_000, (199_000, 13), (89_000, 2.7))))
			.Append(DiskLine(253, 15, "vda15", 0, 0))
			.Append(DiskLine(253, 16, "vdb", Integral(t, 6_000, (5_000, 21)), Integral(t, 120_000, (110_000, 30))))
			.Append(DiskLine(8, 0, "sda", Integral(t, 1_000, (900, 50)), 0))
			.Append(DiskLine(8, 1, "sda1", Integral(t, 900, (800, 50)), 0))
			.Append(DiskLine(7, 0, "loop0", 0, 0))
			.Append("@@mt:blocks\nloop0\nsda\nvda\nvdb\n");
		return text.ToString();
	}

	/// <summary>Seconds the demo machine has been up.</summary>
	private static double Seconds(DateTimeOffset now) => (now - Boot).TotalMilliseconds / 1000;

	/// <summary>
	/// The total of a rate <c>base + sum(amplitude * sin(t / period))</c> from 0 to <paramref name="t"/>, which is what
	/// a counter holds when that is how fast it rises; it never falls, since every base outweighs its waves.
	/// </summary>
	private static double Integral(double t, double rate, params (double Amplitude, double Period)[] waves) =>
		(rate * t) + waves.Sum(wave => wave.Amplitude * wave.Period * (1 - Math.Cos(t / wave.Period)));

	private static string NetLine(string name, long received, long sent) =>
		string.Create(CultureInfo.InvariantCulture, $"{name,6}: {received} {received / 900} 0 0 0 0 0 0 {sent} {sent / 700} 0 0 0 0 0 0\n");

	private static string DiskLine(int major, int minor, string name, double readBytes, double writtenBytes)
	{
		long read = (long)(readBytes / 512);
		long written = (long)(writtenBytes / 512);
		return string.Create(CultureInfo.InvariantCulture, $"{major,4} {minor,7} {name} {read / 8} 0 {read} {read / 20} {written / 8} 0 {written} {written / 15} 0 {written / 10} {written / 9}\n");
	}

	/// <summary>The script inside <c>sh -c '...' sh</c>, with its quotes as they were before the command line was built.</summary>
	private static string Unwrap(string command)
	{
		const string Start = "sh -c '";
		const string End = "' sh";
		return command.StartsWith(Start, StringComparison.Ordinal) && command.EndsWith(End, StringComparison.Ordinal)
			? command[Start.Length..^End.Length].Replace("'\\''", "'", StringComparison.Ordinal)
			: command;
	}

	/// <summary>The value a <c>name='value'</c> line in the script sets; the demo's values never hold a quote.</summary>
	private static string? Assigned(string command, string name)
	{
		Match match = AssignmentPattern().Match(command);
		while (match.Success)
		{
			if (match.Groups["name"].Value == name)
			{
				return match.Groups["value"].Value;
			}

			match = match.NextMatch();
		}

		return null;
	}

	private static long? Number(Regex pattern, string command) =>
		pattern.Match(command) is { Success: true } match && long.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out long value) ? value : null;

	[GeneratedRegex(@"^(?<name>mt_[a-z]+)='(?<value>[^']*)'$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
	private static partial Regex AssignmentPattern();
}
