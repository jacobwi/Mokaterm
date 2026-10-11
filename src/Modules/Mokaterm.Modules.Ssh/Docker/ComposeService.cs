using System.Globalization;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// One compose service as the project view shows it: a row per service rather than per container, so a service scaled
/// to three replicas is one row that says 2/3 and opens to its three containers.
/// </summary>
internal sealed record ComposeService
{
	public required string Name { get; init; }

	public IReadOnlyList<ComposeContainer> Containers { get; init; } = [];

	public int Running => Containers.Count(container => container.IsRunning);

	public int Total => Containers.Count;

	public bool IsScaled => Containers.Count > 1;

	public ComposeServiceState State { get; init; }

	/// <summary>The worst health of its running containers.</summary>
	public DockerHealth Health { get; init; }

	/// <summary>The image of its first container, or empty for a service with none.</summary>
	public string Image => Containers.Count > 0 ? Containers[0].Image : "";

	/// <summary>Every published port of its containers, each once.</summary>
	public IReadOnlyList<string> Ports => [.. Containers.SelectMany(container => container.Ports).Where(port => port.IsPublished).Select(port => port.Display).Distinct(StringComparer.Ordinal)];

	/// <summary>How long its oldest running container has been up, as docker words it (<c>2 hours</c>), or empty.</summary>
	public string Uptime { get; init; } = "";

	/// <summary>Why it is failed or unhealthy: <c>exit 1</c>, <c>restarting</c>, <c>unhealthy</c>. Empty otherwise.</summary>
	public string Problem { get; init; } = "";

	/// <summary>
	/// Services in the order the files define them (when known), then any only the containers name, each with its
	/// containers, state and health worked out.
	/// </summary>
	public static IReadOnlyList<ComposeService> Group(IReadOnlyList<ComposeContainer> containers, IReadOnlyList<string>? defined = null)
	{
		ArgumentNullException.ThrowIfNull(containers);
		List<string> names = [.. defined ?? []];
		foreach (ComposeContainer container in containers)
		{
			if (container.Service.Length > 0 && !names.Contains(container.Service, StringComparer.Ordinal))
			{
				names.Add(container.Service);
			}
		}

		return [.. names.Distinct(StringComparer.Ordinal).Select(name => Summarize(name, [.. containers.Where(container => container.Service == name).OrderBy(container => container.Name, StringComparer.Ordinal)]))];
	}

	/// <summary>The project's one state from its services: unhealthy beats everything, then all, some or none running.</summary>
	public static ComposeProjectHealth HealthOf(IReadOnlyList<ComposeService> services)
	{
		ArgumentNullException.ThrowIfNull(services);
		if (services.Count == 0)
		{
			return ComposeProjectHealth.Empty;
		}

		if (services.Any(service => service.State is ComposeServiceState.Unhealthy or ComposeServiceState.Failed))
		{
			return ComposeProjectHealth.Unhealthy;
		}

		if (services.All(service => service.State == ComposeServiceState.Running))
		{
			return ComposeProjectHealth.Running;
		}

		return services.Any(service => service.Running > 0) ? ComposeProjectHealth.Partial : ComposeProjectHealth.Stopped;
	}

	/// <summary><c>Up 2 hours (healthy)</c> as <c>2 hours</c>; anything that is not an "Up" line as empty.</summary>
	public static string UptimeOf(string status)
	{
		ArgumentNullException.ThrowIfNull(status);
		if (!status.StartsWith("Up ", StringComparison.OrdinalIgnoreCase))
		{
			return "";
		}

		string rest = status[3..];
		int bracket = rest.IndexOf(" (", StringComparison.Ordinal);
		return (bracket < 0 ? rest : rest[..bracket]).Trim();
	}

	/// <summary>The exit code in <c>Exited (137) 3 hours ago</c>, or null.</summary>
	public static int? ExitCodeOf(string status)
	{
		ArgumentNullException.ThrowIfNull(status);
		int open = status.IndexOf('(', StringComparison.Ordinal);
		int close = open < 0 ? -1 : status.IndexOf(')', open);
		return status.StartsWith("Exited", StringComparison.OrdinalIgnoreCase)
			&& close > open
			&& int.TryParse(status.AsSpan(open + 1, close - open - 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int code)
				? code
				: null;
	}

	private static ComposeService Summarize(string name, List<ComposeContainer> containers)
	{
		if (containers.Count == 0)
		{
			return new ComposeService { Name = name, State = ComposeServiceState.NoContainer };
		}

		DockerHealth health = containers.Where(container => container.IsRunning).Select(container => container.Health).DefaultIfEmpty(DockerHealth.None).MaxBy(Severity);
		int running = containers.Count(container => container.IsRunning);
		ComposeContainer? failed = containers.FirstOrDefault(container => container.State == "restarting" || (!container.IsRunning && ExitOf(container) is > 0));

		(ComposeServiceState state, string problem) = (health, running, failed) switch
		{
			(DockerHealth.Unhealthy, _, _) => (ComposeServiceState.Unhealthy, "unhealthy"),
			(_, 0, { State: "restarting" }) => (ComposeServiceState.Failed, "restarting"),
			(_, 0, { } exited) => (ComposeServiceState.Failed, string.Create(CultureInfo.InvariantCulture, $"exit {ExitOf(exited)}")),
			(_, 0, null) => (ComposeServiceState.Stopped, ""),
			_ when running == containers.Count => (ComposeServiceState.Running, ""),
			(_, _, { State: "restarting" }) => (ComposeServiceState.Partial, "restarting"),
			(_, _, { } partlyFailed) => (ComposeServiceState.Partial, string.Create(CultureInfo.InvariantCulture, $"exit {ExitOf(partlyFailed)}")),
			_ => (ComposeServiceState.Partial, ""),
		};

		string uptime = containers.Where(container => container.IsRunning).Select(container => UptimeOf(container.Status)).FirstOrDefault(text => text.Length > 0) ?? "";
		return new ComposeService
		{
			Name = name,
			Containers = containers,
			State = state,
			Health = health,
			Uptime = uptime,
			Problem = problem,
		};
	}

	private static int? ExitOf(ComposeContainer container) => ExitCodeOf(container.Status) ?? container.ExitCode;

	private static int Severity(DockerHealth health) => health switch
	{
		DockerHealth.Unhealthy => 3,
		DockerHealth.Starting => 2,
		DockerHealth.Healthy => 1,
		_ => 0,
	};
}
