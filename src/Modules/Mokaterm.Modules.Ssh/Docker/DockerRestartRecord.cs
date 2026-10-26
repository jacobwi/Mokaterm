using System.Text;

namespace Mokaterm.Modules.Ssh.Docker;

internal sealed record DockerRestartRecord
{
	public required string Id { get; init; }

	public required string Name { get; init; }

	public string? ComposeProject { get; init; }

	public bool IsLooping { get; init; }

	public DateTimeOffset? LoopingSince { get; init; }

	public DockerRestartLoopReason Reason { get; init; }

	public string State { get; init; } = "";

	public int RestartCount { get; init; }

	public int RecentRestarts { get; init; }

	public TimeSpan RecentSpan { get; init; }

	public string RestartPolicy { get; init; } = "no";

	public DockerContainerExit? LastExit { get; init; }

	public IReadOnlyList<TimeSpan> RecentRuns { get; init; } = [];

	public bool Matches(string id, string? name = null) =>
		Id == id
		|| (id.Length >= 12 && Id.StartsWith(id, StringComparison.Ordinal))
		|| (name is { Length: > 0 } && Name == name);

	// A container's name is the server's, which this app never logs.
	private bool PrintMembers(StringBuilder builder)
	{
		builder.Append("Id = ").Append(Id).Append(", IsLooping = ").Append(IsLooping).Append(", RestartCount = ").Append(RestartCount);
		return true;
	}
}
