namespace Mokaterm.DevHost.Demo.Docker;

/// <summary>One container of the demo engine. Mutable: the demo's start, stop and remove change it in place.</summary>
internal sealed class DemoDockerContainer
{
	public required string Id { get; init; }

	public required string Name { get; init; }

	public required string Image { get; init; }

	public required string Command { get; init; }

	public required DateTimeOffset Created { get; init; }

	/// <summary>Lower case, as docker prints it.</summary>
	public string State { get; set; } = "running";

	public DateTimeOffset StartedAt { get; set; }

	public DateTimeOffset FinishedAt { get; set; }

	public int ExitCode { get; set; }

	/// <summary>How often the daemon restarted it under its restart policy, which <c>inspect</c> reports.</summary>
	public int RestartCount { get; set; }

	/// <summary><c>healthy</c>, <c>unhealthy</c>, <c>starting</c> or empty for no health check.</summary>
	public string Health { get; set; } = "";

	/// <summary>As <c>docker ps</c> prints them: <c>0.0.0.0:8080-&gt;80/tcp</c>.</summary>
	public IReadOnlyList<string> Ports { get; init; } = [];

	public IReadOnlyDictionary<string, string> Labels { get; init; } = new Dictionary<string, string>();

	public IReadOnlyList<string> Environment { get; init; } = [];

	public IReadOnlyList<string> Volumes { get; init; } = [];

	public IReadOnlyList<string> Networks { get; init; } = [];

	public string RestartPolicy { get; set; } = "no";

	public bool OomKilled { get; set; }

	public string Error { get; set; } = "";

	public DemoCrashLoop? Loop { get; init; }

	/// <summary>What the container's log says, one entry per line, repeated as time passes.</summary>
	public IReadOnlyList<string> LogLines { get; init; } = ["ready"];

	public bool HasBash { get; init; } = true;

	public double CpuBase { get; init; } = 1.5;

	public long MemoryBytes { get; init; } = 48L * 1024 * 1024;

	public string? Project => Labels.TryGetValue("com.docker.compose.project", out string? project) ? project : null;

	public string? Service => Labels.TryGetValue("com.docker.compose.service", out string? service) ? service : null;

	public bool IsAlive => State is "running" or "paused" or "restarting";
}
