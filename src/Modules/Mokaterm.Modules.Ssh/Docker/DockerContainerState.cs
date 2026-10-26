using System.Text.Json;
using Mokaterm.Abstractions.Containers;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// One container as the health watch reads it, from a line <see cref="DockerCommands.ContainerStates"/> printed: what
/// <c>docker ps</c> cannot say (the restart count, how the last run ended, the restart policy) beside what it can.
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

	public bool OomKilled { get; init; }

	public string Error { get; init; } = "";

	public DateTimeOffset? StartedAt { get; init; }

	public DateTimeOffset? FinishedAt { get; init; }

	public string RestartPolicy { get; init; } = "no";

	public DockerHealth Health { get; init; }

	public string? ComposeProject { get; init; }

	public bool IsAlive => State is "running" or "paused" or "restarting";

	/// <summary>Every line that reads; anything else (a warning, a line cut short) is skipped.</summary>
	public static IReadOnlyList<DockerContainerState> Parse(string? output)
	{
		List<DockerContainerState> states = [];
		foreach (JsonElement item in DockerJsonOutput.ReadObjects(output))
		{
			string id = DockerJson.Text(item, "Id");
			string name = DockerJson.Text(item, "Name").TrimStart('/');
			if (id.Length == 0 || name.Length == 0)
			{
				continue;
			}

			states.Add(new DockerContainerState
			{
				Id = id,
				Name = name,
				RestartCount = DockerJson.Int(item, "RestartCount") ?? 0,
				State = DockerJson.Text(item, "State").Trim().ToLowerInvariant(),
				ExitCode = DockerJson.Int(item, "ExitCode") ?? 0,
				OomKilled = DockerJson.Bool(item, "OOMKilled"),
				Error = DockerJson.Text(item, "Error"),
				StartedAt = DockerJson.Time(item, "StartedAt"),
				FinishedAt = DockerJson.Time(item, "FinishedAt"),
				RestartPolicy = DockerInspectParser.RestartPolicy(DockerJson.Get(item, "RestartPolicy")),
				Health = DockerJson.Text(item, "Health") switch
				{
					"healthy" => DockerHealth.Healthy,
					"unhealthy" => DockerHealth.Unhealthy,
					"starting" => DockerHealth.Starting,
					_ => DockerHealth.None,
				},
				ComposeProject = DockerJson.Text(item, "Project") is { Length: > 0 } project ? project : null,
			});
		}

		return states;
	}
}
