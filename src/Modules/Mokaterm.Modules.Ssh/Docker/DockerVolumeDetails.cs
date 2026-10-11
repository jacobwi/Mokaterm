using System.Text.Json;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>What <c>docker volume inspect</c> says about one volume, and which containers mount it where.</summary>
internal sealed record DockerVolumeDetails
{
	public required string Name { get; init; }

	public string Driver { get; init; } = "";

	/// <summary>Where the volume's files are on the docker host, for example <c>/var/lib/docker/volumes/data/_data</c>.</summary>
	public string Mountpoint { get; init; } = "";

	public string Scope { get; init; } = "";

	public DateTimeOffset? Created { get; init; }

	public IReadOnlyDictionary<string, string> Labels { get; init; } = new Dictionary<string, string>();

	public IReadOnlyDictionary<string, string> Options { get; init; } = new Dictionary<string, string>();

	/// <summary>The containers that mount it, with the path inside each.</summary>
	public IReadOnlyList<DockerVolumeUse> UsedBy { get; init; } = [];

	/// <summary>The volume in <paramref name="output"/>, or null when it printed none.</summary>
	public static DockerVolumeDetails? Parse(string? output)
	{
		JsonElement volume = DockerJson.First(output);
		string name = DockerJson.Text(volume, "Name");
		if (name.Length == 0)
		{
			return null;
		}

		return new DockerVolumeDetails
		{
			Name = name,
			Driver = DockerJson.Text(volume, "Driver"),
			Mountpoint = DockerJson.Text(volume, "Mountpoint"),
			Scope = DockerJson.Text(volume, "Scope"),
			Created = DockerJson.Time(volume, "CreatedAt"),
			Labels = DockerJson.Map(volume, "Labels"),
			Options = DockerJson.Map(volume, "Options"),
		};
	}
}
