using System.Globalization;
using System.Text;
using System.Text.Json;
using Mokaterm.Abstractions.Containers;
using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>Reads <c>docker container inspect</c>, which prints a JSON array holding one object per container named.</summary>
internal static class DockerInspectParser
{
	/// <summary>What a masked value shows instead of itself.</summary>
	public const string Mask = "••••••";

	/// <exception cref="DockerCommandException">The output holds no container.</exception>
	public static DockerContainerDetails Parse(string? output) =>
		ParseAll(output) is [var first, ..] ? first : throw new DockerCommandException(DockerFailure.UnreadableOutput, "docker inspect printed no container.");

	/// <summary>Every container in the output, for an inspect that named several.</summary>
	public static IReadOnlyList<DockerContainerDetails> ParseAll(string? output) =>
		[.. DockerJsonOutput.ReadObjects(output).Where(container => DockerJson.Text(container, "Id").Length > 0).Select(Read)];

	private static DockerContainerDetails Read(JsonElement container)
	{
		JsonElement state = DockerJson.Get(container, "State");
		JsonElement config = DockerJson.Get(container, "Config");
		JsonElement host = DockerJson.Get(container, "HostConfig");
		return new DockerContainerDetails
		{
			Id = DockerJson.Text(container, "Id"),
			Name = DockerJson.Text(container, "Name").TrimStart('/'),
			Image = DockerJson.Text(config, "Image"),
			ImageId = DockerJson.Text(container, "Image"),
			Created = DockerJson.Time(container, "Created"),
			Status = DockerJson.Text(state, "Status"),
			Pid = DockerJson.Int(state, "Pid") is > 0 and int pid ? pid : null,
			ExitCode = DockerJson.Int(state, "ExitCode"),
			Error = DockerJson.Text(state, "Error"),
			OomKilled = DockerJson.Bool(state, "OOMKilled"),
			StartedAt = DockerJson.Time(state, "StartedAt"),
			FinishedAt = DockerJson.Time(state, "FinishedAt"),
			RestartPolicy = RestartPolicy(DockerJson.Get(host, "RestartPolicy")),
			RestartCount = DockerJson.Int(container, "RestartCount") ?? 0,
			Health = Health(DockerJson.Get(state, "Health")),
			Entrypoint = DockerJson.Strings(config, "Entrypoint"),
			Command = DockerJson.Strings(config, "Cmd"),
			WorkingDirectory = DockerJson.Text(config, "WorkingDir"),
			User = DockerJson.Text(config, "User"),
			Hostname = DockerJson.Text(config, "Hostname"),
			NetworkMode = DockerJson.Text(host, "NetworkMode"),
			Privileged = DockerJson.Bool(host, "Privileged"),
			Ports = Ports(DockerJson.Get(container, "NetworkSettings", "Ports")),
			Mounts = Mounts(DockerJson.Get(container, "Mounts")),
			Networks = Networks(DockerJson.Get(container, "NetworkSettings", "Networks")),
			Environment = [.. DockerJson.Strings(config, "Env").Select(DockerVariable.Parse)],
			Labels = DockerJson.Map(config, "Labels"),
			Raw = container,
		};
	}

	/// <summary>
	/// The object as indented JSON. Unless <paramref name="revealEnvironment"/>, every value in <c>Config.Env</c> is
	/// replaced by <see cref="Mask"/>, so the raw view shows no more than the summary does.
	/// </summary>
	public static string RawJson(JsonElement container, bool revealEnvironment)
	{
		using MemoryStream buffer = new();
		using (Utf8JsonWriter writer = new(buffer, new JsonWriterOptions { Indented = true, NewLine = "\n", Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
		{
			Write(writer, container, revealEnvironment ? Masking.None : Masking.LookForConfig);
		}

		return Encoding.UTF8.GetString(buffer.ToArray());
	}

	private enum Masking
	{
		None,
		LookForConfig,
		LookForEnv,
		InEnv,
	}

	private static void Write(Utf8JsonWriter writer, JsonElement element, Masking masking)
	{
		switch (element.ValueKind)
		{
			case JsonValueKind.Object:
				writer.WriteStartObject();
				foreach (JsonProperty property in element.EnumerateObject())
				{
					writer.WritePropertyName(property.Name);
					Masking inner = (masking, property.Name) switch
					{
						(Masking.LookForConfig, "Config") => Masking.LookForEnv,
						(Masking.LookForEnv, "Env") => Masking.InEnv,
						_ => Masking.None,
					};
					Write(writer, property.Value, inner);
				}

				writer.WriteEndObject();
				break;
			case JsonValueKind.Array:
				writer.WriteStartArray();
				foreach (JsonElement item in element.EnumerateArray())
				{
					if (masking == Masking.InEnv && item.ValueKind == JsonValueKind.String)
					{
						DockerVariable variable = DockerVariable.Parse(item.GetString() ?? "");
						writer.WriteStringValue(variable.Value.Length == 0 ? variable.Name + "=" : variable.Name + "=" + Mask);
					}
					else
					{
						Write(writer, item, Masking.None);
					}
				}

				writer.WriteEndArray();
				break;
			default:
				element.WriteTo(writer);
				break;
		}
	}

	private static string RestartPolicy(JsonElement policy)
	{
		string name = DockerJson.Text(policy, "Name");
		int retries = DockerJson.Int(policy, "MaximumRetryCount") ?? 0;
		if (name.Length == 0)
		{
			return "no";
		}

		return name == "on-failure" && retries > 0
			? string.Create(CultureInfo.InvariantCulture, $"on-failure:{retries}")
			: name;
	}

	private static DockerHealthCheck? Health(JsonElement health)
	{
		if (health.ValueKind != JsonValueKind.Object)
		{
			return null;
		}

		DockerHealth status = DockerJson.Text(health, "Status") switch
		{
			"healthy" => DockerHealth.Healthy,
			"unhealthy" => DockerHealth.Unhealthy,
			"starting" => DockerHealth.Starting,
			_ => DockerHealth.None,
		};

		List<DockerHealthProbe> probes = [];
		JsonElement log = DockerJson.Get(health, "Log");
		if (log.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement probe in log.EnumerateArray())
			{
				probes.Add(new DockerHealthProbe(
					DockerJson.Time(probe, "Start"),
					DockerJson.Int(probe, "ExitCode") ?? 0,
					TerminalTextFilter.Plain(DockerJson.Text(probe, "Output")).Trim()));
			}
		}

		return new DockerHealthCheck
		{
			Status = status,
			FailingStreak = DockerJson.Int(health, "FailingStreak") ?? 0,
			Probes = probes,
		};
	}

	/// <summary><c>{"80/tcp": [{"HostIp": "0.0.0.0", "HostPort": "8080"}], "443/tcp": null}</c>.</summary>
	private static List<DockerPort> Ports(JsonElement ports)
	{
		List<DockerPort> list = [];
		if (ports.ValueKind != JsonValueKind.Object)
		{
			return list;
		}

		foreach (JsonProperty property in ports.EnumerateObject())
		{
			int slash = property.Name.IndexOf('/', StringComparison.Ordinal);
			string port = slash < 0 ? property.Name : property.Name[..slash];
			string protocol = slash < 0 ? "tcp" : property.Name[(slash + 1)..];
			if (property.Value.ValueKind != JsonValueKind.Array || property.Value.GetArrayLength() == 0)
			{
				list.Add(new DockerPort("", null, port, protocol));
				continue;
			}

			HashSet<string> seen = new(StringComparer.Ordinal);
			foreach (JsonElement binding in property.Value.EnumerateArray())
			{
				string address = DockerJson.Text(binding, "HostIp");
				string hostPort = DockerJson.Text(binding, "HostPort");
				bool everywhere = address is "" or "0.0.0.0" or "::";
				if (seen.Add((everywhere ? "*" : address) + "|" + hostPort))
				{
					list.Add(new DockerPort(address, hostPort, port, protocol));
				}
			}
		}

		return list;
	}

	private static List<DockerMount> Mounts(JsonElement mounts)
	{
		List<DockerMount> list = [];
		if (mounts.ValueKind != JsonValueKind.Array)
		{
			return list;
		}

		foreach (JsonElement mount in mounts.EnumerateArray())
		{
			list.Add(new DockerMount(
				DockerJson.Text(mount, "Type"),
				DockerJson.Text(mount, "Name"),
				DockerJson.Text(mount, "Source"),
				DockerJson.Text(mount, "Destination"),
				DockerJson.Text(mount, "Mode"),
				DockerJson.Get(mount, "RW").ValueKind != JsonValueKind.False));
		}

		return list;
	}

	private static List<DockerNetworkAttachment> Networks(JsonElement networks)
	{
		List<DockerNetworkAttachment> list = [];
		if (networks.ValueKind != JsonValueKind.Object)
		{
			return list;
		}

		foreach (JsonProperty network in networks.EnumerateObject())
		{
			list.Add(new DockerNetworkAttachment(
				network.Name,
				DockerJson.Text(network.Value, "IPAddress"),
				DockerJson.Text(network.Value, "Gateway"),
				DockerJson.Text(network.Value, "MacAddress"),
				DockerJson.Text(network.Value, "GlobalIPv6Address"),
				DockerJson.Strings(network.Value, "Aliases")));
		}

		return list;
	}
}
