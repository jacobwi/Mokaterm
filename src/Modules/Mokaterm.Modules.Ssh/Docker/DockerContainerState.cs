using System.Globalization;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// One container as the health watch reads it, from a line <see cref="DockerCommands.ContainerStates"/> printed: what
/// <c>docker ps</c> cannot say (the restart count) beside what it can.
/// </summary>
internal sealed record DockerContainerState
{
	public required string Id { get; init; }

	public required string Name { get; init; }

	/// <summary>How often the daemon restarted it under its restart policy; a restart the user asks for does not count.</summary>
	public int RestartCount { get; init; }

	/// <summary>Lower case, as docker prints it.</summary>
	public string State { get; init; } = "";

	public int ExitCode { get; init; }

	public DockerHealth Health { get; init; }

	public string? ComposeProject { get; init; }

	public bool IsAlive => State is "running" or "paused" or "restarting";

	/// <summary>Every line that reads; anything else (a warning, a line cut short) is skipped.</summary>
	public static IReadOnlyList<DockerContainerState> Parse(string? output)
	{
		List<DockerContainerState> states = [];
		foreach (string raw in (output ?? "").Split('\n'))
		{
			string[] parts = raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length < 6
				|| !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int restarts)
				|| !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int exit)
				|| !parts[5].StartsWith('/'))
			{
				continue;
			}

			states.Add(new DockerContainerState
			{
				Id = parts[0],
				RestartCount = restarts,
				State = parts[2].ToLowerInvariant(),
				ExitCode = exit,
				Health = parts[4] switch
				{
					"healthy" => DockerHealth.Healthy,
					"unhealthy" => DockerHealth.Unhealthy,
					"starting" => DockerHealth.Starting,
					_ => DockerHealth.None,
				},
				Name = parts[5].TrimStart('/'),
				ComposeProject = parts.Length > 6 ? parts[6] : null,
			});
		}

		return states;
	}
}
