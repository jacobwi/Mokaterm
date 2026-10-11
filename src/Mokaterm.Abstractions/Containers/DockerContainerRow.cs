using System.Text;
using System.Text.Json;

namespace Mokaterm.Abstractions.Containers;

/// <summary>
/// One line of <c>docker ps --format '{{json .}}'</c>, with the text fields as docker printed them. Docker before 20.10
/// prints no <c>State</c>, so <see cref="State"/> is worked out from the status line when it is missing.
/// </summary>
public sealed record DockerContainerRow
{
	private static readonly (string Prefix, string State)[] StatusPrefixes =
	[
		("Restarting", "restarting"),
		("Exited", "exited"),
		("Created", "created"),
		("Removal", "removing"),
		("Dead", "dead"),
	];

	public required string Id { get; init; }

	/// <summary>The container's own name, without the slash and without the link aliases older versions add.</summary>
	public required string Name { get; init; }

	public string Image { get; init; } = "";

	public string Command { get; init; } = "";

	/// <summary>As docker prints it, for example <c>2024-01-02 03:04:05 +0000 UTC</c>.</summary>
	public string CreatedAt { get; init; } = "";

	/// <summary>For example <c>3 days ago</c>.</summary>
	public string RunningFor { get; init; } = "";

	/// <summary>For example <c>0.0.0.0:8080-&gt;80/tcp, :::8080-&gt;80/tcp</c>.</summary>
	public string Ports { get; init; } = "";

	/// <summary>Lower case: <c>running</c>, <c>exited</c>, <c>paused</c>, <c>restarting</c>, <c>created</c>, <c>removing</c> or <c>dead</c>.</summary>
	public string State { get; init; } = "";

	/// <summary>For example <c>Up 2 hours (healthy)</c> or <c>Exited (0) 3 days ago</c>.</summary>
	public string Status { get; init; } = "";

	public string Size { get; init; } = "";

	/// <summary>Every label as <c>key=value</c>, joined with commas, which is the only form <c>ps</c> prints them in.</summary>
	public string Labels { get; init; } = "";

	/// <summary>Volume names and bind sources, joined with commas.</summary>
	public string Mounts { get; init; } = "";

	/// <summary>Network names, joined with commas.</summary>
	public string Networks { get; init; } = "";

	public bool IsRunning => State is "running" or "restarting";

	/// <summary>Every container in the output. A line without an id is not a container and is skipped.</summary>
	public static IReadOnlyList<DockerContainerRow> Parse(string? output)
	{
		List<DockerContainerRow> rows = [];
		foreach (JsonElement item in DockerJsonOutput.ReadObjects(output))
		{
			string id = DockerJsonOutput.Text(item, "ID");
			if (id.Length == 0)
			{
				continue;
			}

			string status = DockerJsonOutput.Text(item, "Status");
			string state = DockerJsonOutput.Text(item, "State").Trim().ToLowerInvariant();
			rows.Add(new DockerContainerRow
			{
				Id = id,
				Name = FirstName(DockerJsonOutput.Text(item, "Names"), id),
				Image = DockerJsonOutput.Text(item, "Image"),
				Command = DockerJsonOutput.Text(item, "Command"),
				CreatedAt = DockerJsonOutput.Text(item, "CreatedAt"),
				RunningFor = DockerJsonOutput.Text(item, "RunningFor"),
				Ports = DockerJsonOutput.Text(item, "Ports"),
				State = state.Length > 0 ? state : StateFromStatus(status),
				Status = status,
				Size = DockerJsonOutput.Text(item, "Size"),
				Labels = DockerJsonOutput.Text(item, "Labels"),
				Mounts = DockerJsonOutput.Text(item, "Mounts"),
				Networks = DockerJsonOutput.Text(item, "Networks"),
			});
		}

		return rows;
	}

	/// <summary>The state an old docker's status line describes: <c>Up 2 hours (Paused)</c> is paused, <c>Up</c> running.</summary>
	public static string StateFromStatus(string? status)
	{
		string text = status?.Trim() ?? "";
		if (text.StartsWith("Up", StringComparison.OrdinalIgnoreCase))
		{
			return text.Contains("(Paused)", StringComparison.OrdinalIgnoreCase) ? "paused" : "running";
		}

		foreach ((string prefix, string state) in StatusPrefixes)
		{
			if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			{
				return state;
			}
		}

		return "";
	}

	/// <summary>
	/// Docker lists a container under its own name and, on old versions, under every alias a link gave it
	/// (<c>web,app/db</c>); the alias names hold a slash.
	/// </summary>
	private static string FirstName(string names, string id)
	{
		foreach (string name in names.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			string plain = name.TrimStart('/');
			if (plain.Length > 0 && !plain.Contains('/', StringComparison.Ordinal))
			{
				return plain;
			}
		}

		return id.Length > 12 ? id[..12] : id;
	}

	// A container's command line and labels can carry a token, so the generated ToString prints neither.
	private bool PrintMembers(StringBuilder builder)
	{
		builder.Append("Id = ").Append(Id).Append(", Name = ").Append(Name).Append(", State = ").Append(State);
		return true;
	}
}
