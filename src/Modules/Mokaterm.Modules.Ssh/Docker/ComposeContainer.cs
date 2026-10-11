using System.Globalization;
using System.Text.Json;
using Mokaterm.Abstractions.Containers;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// One container of <c>docker compose ps -a --format json</c>. Compose before 2.21 printed one array, later versions one
/// object per line, and the oldest 2.x lacked several fields, so everything but the name may be empty.
/// </summary>
internal sealed record ComposeContainer
{
	public required string Name { get; init; }

	public string Id { get; init; } = "";

	public string Service { get; init; } = "";

	public string Image { get; init; } = "";

	/// <summary>Lower case, as <c>docker ps</c> prints it.</summary>
	public string State { get; init; } = "";

	public string Status { get; init; } = "";

	public DockerHealth Health { get; init; }

	public int? ExitCode { get; init; }

	public IReadOnlyList<DockerPort> Ports { get; init; } = [];

	public bool IsRunning => State == "running";

	/// <summary>A container from the container list as compose would list it, for a compose that prints no JSON.</summary>
	public static ComposeContainer FromContainer(DockerContainer container)
	{
		ArgumentNullException.ThrowIfNull(container);
		return new ComposeContainer
		{
			Name = container.Name,
			Id = container.Id,
			Service = container.ComposeService ?? "",
			Image = container.Image,
			State = container.State,
			Status = container.Status,
			Health = container.Health,
			ExitCode = ComposeService.ExitCodeOf(container.Status),
			Ports = container.Ports,
		};
	}

	public static IReadOnlyList<ComposeContainer> Parse(string? output)
	{
		List<ComposeContainer> containers = [];
		foreach (JsonElement item in DockerJsonOutput.ReadObjects(output))
		{
			string name = DockerJson.Text(item, "Name");
			if (name.Length == 0)
			{
				name = DockerJson.Text(item, "Names");
			}

			if (name.Length == 0)
			{
				continue;
			}

			string status = DockerJson.Text(item, "Status");
			string state = DockerJson.Text(item, "State").Trim().ToLowerInvariant();
			containers.Add(new ComposeContainer
			{
				Name = name,
				Id = DockerJson.Text(item, "ID"),
				Service = DockerJson.Text(item, "Service"),
				Image = DockerJson.Text(item, "Image"),
				State = state.Length > 0 ? state : DockerContainerRow.StateFromStatus(status),
				Status = status,
				Health = DockerJson.Text(item, "Health") switch
				{
					"healthy" => DockerHealth.Healthy,
					"unhealthy" => DockerHealth.Unhealthy,
					"starting" => DockerHealth.Starting,
					_ => DockerContainer.HealthFromStatus(status),
				},
				ExitCode = DockerJson.Int(item, "ExitCode"),
				Ports = PortsOf(item),
			});
		}

		return containers;
	}

	/// <summary>
	/// The <c>Publishers</c> array (<c>URL</c>, <c>PublishedPort</c>, <c>TargetPort</c>, <c>Protocol</c>), or the ports text
	/// older versions print instead. A publisher with port 0 is only exposed.
	/// </summary>
	private static IReadOnlyList<DockerPort> PortsOf(JsonElement item)
	{
		JsonElement publishers = DockerJson.Get(item, "Publishers");
		if (publishers.ValueKind != JsonValueKind.Array)
		{
			return DockerPorts.Parse(DockerJson.Text(item, "Ports"));
		}

		List<DockerPort> ports = [];
		HashSet<string> seen = new(StringComparer.Ordinal);
		foreach (JsonElement publisher in publishers.EnumerateArray())
		{
			int target = DockerJson.Int(publisher, "TargetPort") ?? 0;
			int published = DockerJson.Int(publisher, "PublishedPort") ?? 0;
			string address = DockerJson.Text(publisher, "URL");
			string protocol = DockerJson.Text(publisher, "Protocol") is { Length: > 0 } named ? named : "tcp";
			if (target == 0)
			{
				continue;
			}

			bool everywhere = address is "" or "0.0.0.0" or "::";
			string key = string.Create(CultureInfo.InvariantCulture, $"{(everywhere ? "*" : address)}|{published}|{target}|{protocol}");
			if (seen.Add(key))
			{
				ports.Add(new DockerPort(
					address,
					published == 0 ? null : published.ToString(CultureInfo.InvariantCulture),
					target.ToString(CultureInfo.InvariantCulture),
					protocol));
			}
		}

		return ports;
	}
}
