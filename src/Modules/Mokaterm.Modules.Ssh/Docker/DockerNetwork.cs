using System.Text.Json;
using Mokaterm.Abstractions.Containers;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>One line of <c>docker network ls --format '{{json .}}'</c>, with the containers that are on it.</summary>
internal sealed record DockerNetwork
{
	/// <summary>The networks the daemon makes itself and refuses to remove.</summary>
	public static readonly IReadOnlySet<string> BuiltIn = new HashSet<string>(StringComparer.Ordinal)
	{
		"bridge",
		"host",
		"none",
		"docker_gwbridge",
		"ingress",
	};

	public required string Id { get; init; }

	public required string Name { get; init; }

	public string Driver { get; init; } = "";

	public string Scope { get; init; } = "";

	public bool Internal { get; init; }

	public bool Ipv6 { get; init; }

	/// <summary>Names of the containers on it, running or stopped.</summary>
	public IReadOnlyList<string> Containers { get; init; } = [];

	public bool IsBuiltIn => BuiltIn.Contains(Name);

	public bool InUse => Containers.Count > 0;

	/// <summary>True when it can be removed: made by someone, and no container is on it.</summary>
	public bool IsRemovable => !IsBuiltIn && !InUse;

	public string ShortId => Id.Length > 12 ? Id[..12] : Id;

	public bool Matches(string query)
	{
		ArgumentNullException.ThrowIfNull(query);
		string needle = query.Trim();
		return needle.Length == 0
			|| Name.Contains(needle, StringComparison.OrdinalIgnoreCase)
			|| Driver.Contains(needle, StringComparison.OrdinalIgnoreCase)
			|| Id.StartsWith(needle, StringComparison.OrdinalIgnoreCase);
	}

	/// <param name="output">The listing.</param>
	/// <param name="containers">Every container, whose network names say which networks are in use.</param>
	public static IReadOnlyList<DockerNetwork> Parse(string? output, IReadOnlyList<DockerContainer> containers)
	{
		ArgumentNullException.ThrowIfNull(containers);
		Dictionary<string, List<string>> users = new(StringComparer.Ordinal);
		foreach (DockerContainer container in containers)
		{
			foreach (string network in container.NetworkNames)
			{
				if (!users.TryGetValue(network, out List<string>? names))
				{
					names = [];
					users[network] = names;
				}

				names.Add(container.Name);
			}
		}

		List<DockerNetwork> networks = [];
		foreach (JsonElement item in DockerJsonOutput.ReadObjects(output))
		{
			string id = DockerJson.Text(item, "ID");
			string name = DockerJson.Text(item, "Name");
			if (id.Length == 0 || name.Length == 0)
			{
				continue;
			}

			networks.Add(new DockerNetwork
			{
				Id = id,
				Name = name,
				Driver = DockerJson.Text(item, "Driver"),
				Scope = DockerJson.Text(item, "Scope"),
				Internal = DockerJson.Text(item, "Internal") == "true",
				Ipv6 = DockerJson.Text(item, "IPv6") == "true",
				Containers = users.TryGetValue(name, out List<string>? on) ? on : [],
			});
		}

		return networks;
	}
}
