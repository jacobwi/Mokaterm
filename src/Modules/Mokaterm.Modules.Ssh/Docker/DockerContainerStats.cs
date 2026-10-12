using System.Globalization;
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

	/// <summary>The memory in use, the first half of <see cref="Memory"/>.</summary>
	public long? MemoryBytes => DockerSizes.ParsePair(Memory).First;

	/// <summary>Received and sent together, for sorting.</summary>
	public long? NetworkBytes => Sum(NetworkIo);

	/// <summary>Read and written together, for sorting.</summary>
	public long? BlockBytes => Sum(BlockIo);

	public int? PidCount => int.TryParse(Pids, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) ? count : null;

	/// <summary>The CPU figure as a share of every core, 0 to 100, which is what a bar shows.</summary>
	public double? CpuShare(int cores) => CpuValue is { } cpu ? cpu / Math.Max(1, cores) : null;

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

	private static long? Sum(string pair) => DockerSizes.ParsePair(pair) switch
	{
		({ } first, { } second) => first + second,
		({ } first, null) => first,
		_ => null,
	};
}
