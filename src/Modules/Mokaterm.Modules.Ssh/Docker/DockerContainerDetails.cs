using System.Text;
using System.Text.Json;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>What <c>docker inspect</c> says about one container, read into what the summary shows, plus the JSON itself.</summary>
internal sealed record DockerContainerDetails
{
	public required string Id { get; init; }

	public required string Name { get; init; }

	/// <summary>The image as the container was created from it, such as <c>nginx:1.27</c>.</summary>
	public string Image { get; init; } = "";

	/// <summary>The image's id.</summary>
	public string ImageId { get; init; } = "";

	public DateTimeOffset? Created { get; init; }

	public string Status { get; init; } = "";

	public int? Pid { get; init; }

	public int? ExitCode { get; init; }

	/// <summary>Why the container could not start, when the daemon recorded a reason.</summary>
	public string Error { get; init; } = "";

	public bool OomKilled { get; init; }

	public DateTimeOffset? StartedAt { get; init; }

	public DateTimeOffset? FinishedAt { get; init; }

	/// <summary><c>no</c>, <c>always</c>, <c>unless-stopped</c> or <c>on-failure</c>, with its retry count for the last.</summary>
	public string RestartPolicy { get; init; } = "";

	public int RestartCount { get; init; }

	public DockerHealthCheck? Health { get; init; }

	public IReadOnlyList<string> Entrypoint { get; init; } = [];

	public IReadOnlyList<string> Command { get; init; } = [];

	public string WorkingDirectory { get; init; } = "";

	public string User { get; init; } = "";

	public string Hostname { get; init; } = "";

	public string NetworkMode { get; init; } = "";

	public bool Privileged { get; init; }

	public IReadOnlyList<DockerPort> Ports { get; init; } = [];

	public IReadOnlyList<DockerMount> Mounts { get; init; } = [];

	public IReadOnlyList<DockerNetworkAttachment> Networks { get; init; } = [];

	public IReadOnlyList<DockerVariable> Environment { get; init; } = [];

	public IReadOnlyDictionary<string, string> Labels { get; init; } = new Dictionary<string, string>();

	/// <summary>The object docker printed, for the raw view, which masks the environment itself.</summary>
	public JsonElement Raw { get; init; }

	// The environment, the command and the raw JSON can all carry a password.
	private bool PrintMembers(StringBuilder builder)
	{
		builder.Append("Id = ").Append(Id).Append(", Name = ").Append(Name).Append(", Status = ").Append(Status);
		return true;
	}
}
