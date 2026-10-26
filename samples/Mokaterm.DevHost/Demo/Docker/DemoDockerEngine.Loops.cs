using System.Globalization;
using System.Text;

namespace Mokaterm.DevHost.Demo.Docker;

// Two containers that crash in a loop on a clock, so the restart loop detection has something to find without Docker:
// monitoring's prometheus is killed out of memory while it replays its write-ahead log, and mail-relay exits 1 because
// its password is missing. Each run logs its lines spread over the run, and the last one is the crash.
internal sealed partial class DemoDockerEngine
{
	private const string Prometheus = "monitoring-prometheus-1";
	private const string MailRelay = "mail-relay";
	private const string RestartFlag = "--restart=";
	private const string NeverFinished = "0001-01-01T00:00:00Z";

	private void SeedLoops(DateTimeOffset now)
	{
		_containers.Add(new DemoDockerContainer
		{
			Id = HexId("prometheus"),
			Name = Prometheus,
			Image = "prom/prometheus:v2.54.1",
			Command = "/bin/prometheus --config.file=/etc/prometheus/prometheus.yml --storage.tsdb.path=/prometheus",
			Created = now.AddDays(-15),
			RestartPolicy = "always",
			Ports = ["0.0.0.0:9090->9090/tcp"],
			Labels = new Dictionary<string, string>
			{
				["com.docker.compose.project"] = "monitoring",
				["com.docker.compose.service"] = "prometheus",
				["com.docker.compose.project.working_dir"] = MonitoringFolder,
				["com.docker.compose.project.config_files"] = $"{MonitoringFolder}/docker-compose.yml,{MonitoringFolder}/docker-compose.override.yml",
			},
			Networks = ["bridge"],
			HasBash = false,
			CpuBase = 4,
			MemoryBytes = 498L * 1024 * 1024,
			LogLines =
			[
				"level=info caller=main.go:601 msg=\"Starting Prometheus Server\" mode=server version=2.54.1",
				"level=info caller=main.go:1156 msg=\"Starting TSDB ...\"",
				"level=info caller=head.go:626 component=tsdb msg=\"Replaying on-disk memory mappable chunks if any\"",
				"level=info caller=head.go:713 component=tsdb msg=\"Replaying WAL, this may take a while\"",
				"level=info caller=head.go:785 component=tsdb msg=\"WAL segment loaded\" segment=412 maxSegment=1288",
				"level=info caller=head.go:785 component=tsdb msg=\"WAL segment loaded\" segment=903 maxSegment=1288",
			],
			Loop = new DemoCrashLoop
			{
				RunsFor = TimeSpan.FromSeconds(6),
				WaitsFor = TimeSpan.FromSeconds(5),
				ExitCode = 137,
				OomKilled = true,
				Origin = now.AddSeconds(-2),
				RestartsBefore = 41,
			},
		});
		_containers.Add(new DemoDockerContainer
		{
			Id = HexId("mail-relay"),
			Name = MailRelay,
			Image = "alpine:3.20",
			Command = "/bin/sh /app/relay.sh",
			Created = now.AddDays(-2),
			RestartPolicy = "unless-stopped",
			Networks = ["bridge"],
			Environment = ["RELAY_HOST=mail", "RELAY_PORT=587", "RELAY_USER=notifications"],
			HasBash = false,
			CpuBase = 0.3,
			MemoryBytes = 6L * 1024 * 1024,
			LogLines =
			[
				"relay: starting, version 1.4.2",
				"relay: reading /etc/relay/relay.conf",
				"relay: connecting to mail:587",
				"relay: error: authentication failed: RELAY_PASSWORD is not set",
				"relay: exiting with status 1",
			],
			Loop = new DemoCrashLoop
			{
				RunsFor = TimeSpan.FromSeconds(9),
				WaitsFor = TimeSpan.FromSeconds(4),
				ExitCode = 1,
				Origin = now.AddSeconds(-7),
				RestartsBefore = 12,
			},
		});

		foreach (DemoDockerContainer container in _containers.Where(container => container.Loop is not null))
		{
			MoveLoop(container, now);
		}
	}

	private static void MoveLoop(DemoDockerContainer container, DateTimeOffset now)
	{
		DemoCrashLoop loop = container.Loop!;
		if (loop.EndsAt is { } ends && now >= ends)
		{
			container.State = "exited";
			container.ExitCode = loop.ExitCode;
			container.OomKilled = loop.OomKilled;
			container.StartedAt = ends - loop.RunsFor;
			container.FinishedAt = ends;
			return;
		}

		long run = loop.RunAt(now);
		DateTimeOffset started = loop.StartOf(run);
		container.RestartCount = loop.RestartsBefore + (int)run;
		container.StartedAt = started;
		if (now - started < loop.RunsFor)
		{
			// Docker clears how the last run ended when it starts the container again.
			container.State = "running";
			container.ExitCode = 0;
			container.OomKilled = false;
			if (container.RestartCount > 0)
			{
				container.FinishedAt = started - loop.WaitsFor;
			}
		}
		else
		{
			container.State = "restarting";
			container.ExitCode = loop.ExitCode;
			container.OomKilled = loop.OomKilled;
			container.FinishedAt = started + loop.RunsFor;
		}
	}

	// docker start and docker restart set the restart count back to zero.
	private void StartLoop(DemoDockerContainer container)
	{
		if (container.Loop is not { } loop)
		{
			return;
		}

		DateTimeOffset now = _time.GetUtcNow();
		loop.Origin = now;
		loop.RestartsBefore = 0;
		loop.EndsAt = container.RestartPolicy == "no" ? now + loop.RunsFor : null;
		container.RestartCount = 0;
	}

	private void SetRestartPolicy(DemoDockerContainer container, string policy)
	{
		container.RestartPolicy = policy;
		if (container.Loop is not { } loop || !container.IsAlive)
		{
			return;
		}

		if (policy != "no")
		{
			loop.EndsAt = null;
			return;
		}

		// A run going now is the last one; a container waiting to restart is still started once more, as docker does.
		DateTimeOffset now = _time.GetUtcNow();
		DateTimeOffset started = loop.StartOf(loop.RunAt(now));
		loop.EndsAt ??= now - started < loop.RunsFor ? started + loop.RunsFor : started + loop.Period + loop.RunsFor;
	}

	private DemoDockerResult LoopLogs(DemoDockerContainer container, int tail, string? since)
	{
		DemoCrashLoop loop = container.Loop!;
		DateTimeOffset end = container.IsAlive ? _time.GetUtcNow() : container.FinishedAt;
		List<(string Stamp, string Text)> lines = [];
		bool reachedSince = false;
		for (long run = loop.RunAt(end); run >= -loop.RestartsBefore && lines.Count < tail && !reachedSince; run--)
		{
			DateTimeOffset started = loop.StartOf(run);
			for (int index = container.LogLines.Count - 1; index >= 0 && lines.Count < tail && !reachedSince; index--)
			{
				DateTimeOffset at = started + (loop.RunsFor * (index + 1) / (container.LogLines.Count + 1));
				string stamp = at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'00Z'", CultureInfo.InvariantCulture);
				reachedSince = since is not null && string.CompareOrdinal(stamp, since) < 0;
				if (at <= end && !reachedSince)
				{
					lines.Add((stamp, container.LogLines[index]));
				}
			}
		}

		StringBuilder output = new();
		StringBuilder error = new();
		foreach ((string stamp, string text) in Enumerable.Reverse(lines))
		{
			bool toError = text.Contains("error", StringComparison.OrdinalIgnoreCase) || text.StartsWith("warn", StringComparison.Ordinal);
			(toError ? error : output).Append(stamp).Append(' ').Append(text).Append('\n');
		}

		return new DemoDockerResult(0, output.ToString(), error.ToString());
	}

	private static string FinishedText(DemoDockerContainer container) =>
		container.FinishedAt == default ? NeverFinished : container.FinishedAt.ToString("o", CultureInfo.InvariantCulture);

	private static object PolicyOf(DemoDockerContainer container)
	{
		string[] parts = container.RestartPolicy.Split(':');
		return new
		{
			Name = parts[0],
			MaximumRetryCount = parts.Length > 1 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : 0,
		};
	}
}
