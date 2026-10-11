using System.Text.Json;
using Mokaterm.Abstractions.Containers;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>One line of <c>docker system df --format '{{json .}}'</c>: images, containers, local volumes or build cache.</summary>
internal sealed record DockerDiskUsage
{
	public required string Type { get; init; }

	public string TotalCount { get; init; } = "";

	public string Active { get; init; } = "";

	/// <summary>Docker's own text, for example <c>4.2GB</c>.</summary>
	public string Size { get; init; } = "";

	/// <summary>Docker's own text, for example <c>2.1GB (50%)</c>.</summary>
	public string Reclaimable { get; init; } = "";

	public long? ReclaimableBytes => DockerSizes.Parse(Reclaimable);

	public static IReadOnlyList<DockerDiskUsage> Parse(string? output)
	{
		List<DockerDiskUsage> rows = [];
		foreach (JsonElement item in DockerJsonOutput.ReadObjects(output))
		{
			string type = DockerJson.Text(item, "Type");
			if (type.Length == 0)
			{
				continue;
			}

			rows.Add(new DockerDiskUsage
			{
				Type = type,
				TotalCount = DockerJson.Text(item, "TotalCount"),
				Active = DockerJson.Text(item, "Active"),
				Size = DockerJson.Text(item, "Size"),
				Reclaimable = DockerJson.Text(item, "Reclaimable"),
			});
		}

		return rows;
	}
}
