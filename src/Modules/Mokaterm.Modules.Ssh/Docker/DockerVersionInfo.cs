using System.Text.Json;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// <c>docker version --format '{{json .}}'</c>: the client, and the server when the daemon answered. A daemon that did not
/// answer leaves <c>Server</c> null while the client part is still printed.
/// </summary>
internal sealed record DockerVersionInfo
{
	public string ClientVersion { get; init; } = "";

	public string ClientApiVersion { get; init; } = "";

	/// <summary>The docker context the client used, on versions that print one.</summary>
	public string Context { get; init; } = "";

	public string ServerVersion { get; init; } = "";

	public string ServerApiVersion { get; init; } = "";

	/// <summary>For example <c>Docker Engine - Community</c>.</summary>
	public string Platform { get; init; } = "";

	public string Os { get; init; } = "";

	public string Architecture { get; init; } = "";

	public string KernelVersion { get; init; } = "";

	public bool HasServer => ServerVersion.Length > 0;

	/// <summary>Null when the output is not the version object at all.</summary>
	public static DockerVersionInfo? Parse(string? output)
	{
		JsonElement root = DockerJson.First(output);
		if (root.ValueKind != JsonValueKind.Object)
		{
			return null;
		}

		JsonElement client = DockerJson.Get(root, "Client");
		JsonElement server = DockerJson.Get(root, "Server");
		if (client.ValueKind != JsonValueKind.Object && server.ValueKind != JsonValueKind.Object)
		{
			return null;
		}

		return new DockerVersionInfo
		{
			ClientVersion = DockerJson.Text(client, "Version"),
			ClientApiVersion = DockerJson.Text(client, "ApiVersion"),
			Context = DockerJson.Text(client, "Context"),
			ServerVersion = DockerJson.Text(server, "Version"),
			ServerApiVersion = DockerJson.Text(server, "ApiVersion"),
			Platform = DockerJson.Text(DockerJson.Get(server, "Platform"), "Name"),
			Os = DockerJson.Text(server, "Os"),
			Architecture = DockerJson.Text(server, "Arch"),
			KernelVersion = DockerJson.Text(server, "KernelVersion"),
		};
	}
}
