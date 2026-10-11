using System.Text.Json;
using Mokaterm.Abstractions.Containers;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// One line of <c>docker stats --no-stream --format '{{json .}}'</c>: docker's own text for each figure, which is what the
/// page prints, and the numbers read out of it for the bars.
/// </summary>
internal sealed record DockerContainerStats
{
	public required string Id { get; init; }

	public string Name { get; init; } = "";

	/// <summary>For example <c>12.34%</c>; past 100% on a machine with several cores.</summary>
	public string Cpu { get; init; } = "";

	/// <summary>For example <c>10.2MiB / 7.6GiB</c>.</summary>
	public string Memory { get; init; } = "";

	public string MemoryPercent { get; init; } = "";

	/// <summary>Received and sent, for example <c>1.2kB / 0B</c>.</summary>
	public string NetworkIo { get; init; } = "";

	/// <summary>Read and written, for example <c>4.1MB / 0B</c>.</summary>
	public string BlockIo { get; init; } = "";

	public string Pids { get; init; } = "";

	public double? CpuValue => DockerSizes.ParsePercent(Cpu);

	public double? MemoryPercentValue => DockerSizes.ParsePercent(MemoryPercent);

	public static IReadOnlyList<DockerContainerStats> Parse(string? output)
	{
		List<DockerContainerStats> rows = [];
		foreach (JsonElement item in DockerJsonOutput.ReadObjects(output))
		{
			string id = DockerJson.Text(item, "ID");
			if (id.Length == 0)
			{
				// Old versions print "Container" and no ID.
				id = DockerJson.Text(item, "Container");
			}

			if (id.Length == 0)
			{
				continue;
			}

			rows.Add(new DockerContainerStats
			{
				Id = id,
				Name = DockerJson.Text(item, "Name"),
				Cpu = DockerJson.Text(item, "CPUPerc"),
				Memory = DockerJson.Text(item, "MemUsage"),
				MemoryPercent = DockerJson.Text(item, "MemPerc"),
				NetworkIo = DockerJson.Text(item, "NetIO"),
				BlockIo = DockerJson.Text(item, "BlockIO"),
				Pids = DockerJson.Text(item, "PIDs"),
			});
		}

		return rows;
	}
}
