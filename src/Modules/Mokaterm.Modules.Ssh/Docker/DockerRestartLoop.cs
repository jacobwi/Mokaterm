using Mokaterm.Modules.Ssh.Tools;

namespace Mokaterm.Modules.Ssh.Docker;

internal static class DockerRestartLoop
{
	// One restart is a crash, and a second is often a service that started before something it needs was up. A third
	// within the window says restarting is not fixing it.
	public const int Restarts = 3;

	// Docker waits at most a minute between two restarts, so a loop at its slowest pace still passes the count several
	// times over, and so does a process that dies after a few minutes of work.
	public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

	// Docker resets its back-off once a run lasted ten seconds, so a wait near that would let a container go between two
	// crashes. One that dies every few minutes has to outlast twice its longest run as well.
	public static readonly TimeSpan StableFor = TimeSpan.FromMinutes(2);

	public const int StableRunFactor = 2;

	public static bool WillRestart(string policy) => policy.Length > 0 && policy != "no";

	public static bool IsRestartingNow(DockerContainerState state)
	{
		ArgumentNullException.ThrowIfNull(state);
		return state.State == "restarting" && WillRestart(state.RestartPolicy);
	}

	// A restarting container whose policy became no still runs once more, but docker will not start it after that.
	public static bool HasStopped(DockerContainerState state)
	{
		ArgumentNullException.ThrowIfNull(state);
		return state.State is not ("running" or "restarting") || !WillRestart(state.RestartPolicy);
	}

	public static (int Count, TimeSpan Over) RecentRestarts(IReadOnlyList<DockerRestartSample> samples)
	{
		ArgumentNullException.ThrowIfNull(samples);
		if (samples.Count < 2)
		{
			return (0, TimeSpan.Zero);
		}

		DockerRestartSample newest = samples[^1];
		DockerRestartSample previous = samples[^2];
		if (newest.At - previous.At > Window)
		{
			return (newest.Count - previous.Count, newest.At - previous.At);
		}

		DockerRestartSample earliest = samples.First(sample => newest.At - sample.At <= Window);
		return (newest.Count - earliest.Count, newest.At - earliest.At);
	}

	public static bool RoseTooOften(IReadOnlyList<DockerRestartSample> samples)
	{
		(int count, TimeSpan over) = RecentRestarts(samples);

		// Two reads further apart than the window (the manager was hidden) only give an average: restarts enough to fill
		// every window of the gap mean at least one window held the full count.
		int windows = over > Window ? (int)Math.Ceiling(over / Window) : 1;
		return count > 0 && count >= Restarts * windows;
	}

	public static TimeSpan StableAfter(IEnumerable<TimeSpan> runs)
	{
		ArgumentNullException.ThrowIfNull(runs);
		TimeSpan outlast = runs.DefaultIfEmpty(TimeSpan.Zero).Max() * StableRunFactor;
		return outlast > StableFor ? outlast : StableFor;
	}

	// Docker keeps one log across restarts, so once the container runs again the tail ends with the new run's lines.
	// Null when every line is the new run's.
	public static IReadOnlyList<LogLine>? BeforeRun(IReadOnlyList<LogLine> lines, DateTimeOffset runStarted)
	{
		ArgumentNullException.ThrowIfNull(lines);
		int cut = lines.Count;
		for (int index = 0; index < lines.Count; index++)
		{
			if (DockerJson.ParseTime(lines[index].Timestamp) is { } at && at >= runStarted)
			{
				cut = index;
				break;
			}
		}

		return cut switch
		{
			0 when lines.Count > 0 => null,
			_ when cut == lines.Count => lines,
			_ => [.. lines.Take(cut)],
		};
	}

	public static DateTimeOffset? RunningAgainSince(DockerContainerDetails details)
	{
		ArgumentNullException.ThrowIfNull(details);
		return details is { Status: "running", StartedAt: { } started, FinishedAt: { } finished } && started > finished ? started : null;
	}
}
