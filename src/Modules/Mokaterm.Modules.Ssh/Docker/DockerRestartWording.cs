using System.Globalization;

namespace Mokaterm.Modules.Ssh.Docker;

internal static class DockerRestartWording
{
	public static string Notice(DockerRestartRecord record)
	{
		ArgumentNullException.ThrowIfNull(record);
		return $"{record.Name} is in a restart loop: {Why(record)}.{LastExit(record.LastExit)}";
	}

	public static string Summary(DockerRestartRecord record)
	{
		ArgumentNullException.ThrowIfNull(record);
		string why = Why(record);
		return string.Concat(why[..1].ToUpperInvariant(), why[1..], ".", LastExit(record.LastExit));
	}

	public static string Count(int containers) => containers == 1
		? "1 container keeps restarting"
		: string.Create(CultureInfo.CurrentCulture, $"{containers} containers keep restarting");

	public static string Times(int count) => count == 1 ? "once" : string.Create(CultureInfo.CurrentCulture, $"{count} times");

	public static string Within(TimeSpan span) => span.TotalMinutes switch
	{
		< 1 => "in under a minute",
		< 1.5 => "in a minute",
		< 90 => string.Create(CultureInfo.CurrentCulture, $"in {(int)Math.Round(span.TotalMinutes, MidpointRounding.AwayFromZero)} minutes"),
		_ => string.Create(CultureInfo.CurrentCulture, $"in {(int)Math.Round(span.TotalHours, MidpointRounding.AwayFromZero)} hours"),
	};

	public static string Policy(string policy)
	{
		ArgumentNullException.ThrowIfNull(policy);
		const string OnFailure = "on-failure:";
		return policy switch
		{
			"" or "no" => "no: Docker does not start it again",
			"always" => "always: Docker starts it again whenever it stops",
			"unless-stopped" => "unless-stopped: Docker starts it again unless someone stopped it",
			"on-failure" => "on-failure: Docker starts it again after an error",
			_ when policy.StartsWith(OnFailure, StringComparison.Ordinal) =>
				$"{policy}: Docker starts it again after an error, {policy[OnFailure.Length..]} times at most",
			_ => policy,
		};
	}

	public static string RunLength(TimeSpan run) => run.TotalSeconds switch
	{
		< 1 => "under a second",
		< 90 => string.Create(CultureInfo.CurrentCulture, $"{(int)Math.Round(run.TotalSeconds)} s"),
		< 5400 => string.Create(CultureInfo.CurrentCulture, $"{(int)Math.Round(run.TotalMinutes)} min"),
		_ => string.Create(CultureInfo.CurrentCulture, $"{run.TotalHours:0.#} h"),
	};

	public static string Runs(IReadOnlyList<TimeSpan> newestFirst)
	{
		ArgumentNullException.ThrowIfNull(newestFirst);
		return newestFirst.Count switch
		{
			0 => "",
			1 => RunLength(newestFirst[0]),
			_ => string.Join(", ", newestFirst.Select(RunLength)) + ", newest first",
		};
	}

	private static string Why(DockerRestartRecord record) =>
		record.Reason == DockerRestartLoopReason.RoseTooOften && record.RecentRestarts > 0
			? $"Docker restarted it {Times(record.RecentRestarts)} {Within(record.RecentSpan)}"
			: $"it stopped again and Docker is about to restart it ({Times(record.RestartCount)} so far)";

	private static string LastExit(DockerContainerExit? exit) =>
		exit is null ? "" : $" Its last exit was {DockerExitCodes.Describe(exit.ExitCode, exit.OomKilled)}.";
}
