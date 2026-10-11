using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Mokaterm.DevHost.Demo.Docker;

/// <summary>The <c>docker</c> commands the manager sends, answered from the demo's state.</summary>
internal sealed partial class DemoDockerEngine
{
	private const string ServerVersion = "27.3.1";

	private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

	private static readonly string[] SecurityOptions = ["name=apparmor", "name=seccomp,profile=builtin", "name=cgroupns"];

	private DemoDockerResult Docker(string[] words) => words switch
	{
		["version", ..] => DemoDockerResult.Ok(Line(new
		{
			Client = new { Version = ServerVersion, ApiVersion = "1.47", Context = "default", Os = "linux", Arch = "amd64" },
			Server = new { Platform = new { Name = "Docker Engine - Community" }, Version = ServerVersion, ApiVersion = "1.47", Os = "linux", Arch = "amd64", KernelVersion = "6.8.0-45-generic" },
		}) + "\n"),
		["info", ..] => Info(),
		["ps", .. var listing] => DemoDockerResult.Ok(string.Concat(Listed(listing).Select(container => Line(PsRow(container)) + "\n"))),
		["container", "inspect", var name] => Inspect(name),
		["container", "inspect", .. var names] => InspectMany(names),
		["volume", "inspect", var volume] => InspectVolume(volume),
		["stats", .. var rest] => Stats(rest.Where(word => !word.StartsWith('-') && !word.Contains("{{", StringComparison.Ordinal))),
		["logs", .. var rest] => Logs(rest),
		["exec", var name, "sh", "-c", "command -v bash"] => FindBash(name),
		["exec", .. var execution] => Exec(execution),
		["start", var name] => Change(name, container => Start(container)),
		["restart", var name] => Change(name, container => Start(container)),
		["unpause", var name] => Change(name, container => container.State = "running"),
		["stop", var name] => Change(name, container => Stop(container, 0)),
		["kill", var name] => Change(name, container => Stop(container, 137)),
		["pause", var name] => Change(name, container => container.State = "paused"),
		["rm", .. var rest] => Remove(rest),
		["image", "ls", ..] => DemoDockerResult.Ok(string.Concat(_images.Select(image => Line(ImageRow(image)) + "\n"))),
		["pull", var reference] => Pull(reference),
		["image", "rm", .. var rest] => RemoveImage(rest),
		["image", "prune", ..] => PruneImages(),
		["volume", "ls", "--quiet", ..] => DemoDockerResult.Ok(string.Concat(_volumes.Where(volume => !VolumeInUse(volume)).Select(volume => volume + "\n"))),
		["volume", "ls", ..] => DemoDockerResult.Ok(string.Concat(_volumes.Select(volume => Line(VolumeRow(volume)) + "\n"))),
		["volume", "create", var name] => CreateVolume(name),
		["volume", "rm", .. var names] => RemoveVolumes(names),
		["network", "ls", ..] => DemoDockerResult.Ok(string.Concat(_networks.Select(network => Line(NetworkRow(network)) + "\n"))),
		["network", "create", var name] => CreateNetwork(name),
		["network", "rm", .. var names] => RemoveNetworks(names),
		["system", "df", ..] => DiskUsage(),
		["system", "prune", ..] => SystemPrune(),
		_ => DemoDockerResult.Fail(1, $"docker: '{string.Join(' ', words)}' is not a docker command the demo knows.\nSee 'docker --help'"),
	};

	/// <summary>Every container, or those that mount the volume a <c>--filter volume=NAME</c> names.</summary>
	private IEnumerable<DemoDockerContainer> Listed(string[] words)
	{
		string? volume = words.Select(word => word.StartsWith("volume=", StringComparison.Ordinal) ? word[7..] : null).FirstOrDefault(name => name is not null);
		return volume is null ? _containers : _containers.Where(container => container.Volumes.Contains(volume));
	}

	private DemoDockerResult Info() => DemoDockerResult.Ok(Line(new
	{
		Containers = _containers.Count,
		ContainersRunning = _containers.Count(container => container.State == "running"),
		ContainersPaused = _containers.Count(container => container.State == "paused"),
		ContainersStopped = _containers.Count(container => !container.IsAlive),
		Images = _images.Count,
		Driver = "overlay2",
		DockerRootDir = "/var/lib/docker",
		OperatingSystem = "Ubuntu 24.04.1 LTS",
		KernelVersion = "6.8.0-45-generic",
		Architecture = "x86_64",
		NCPU = 4,
		MemTotal = 8_201_457_664L,
		Name = "demo-docker",
		SecurityOptions = SecurityOptions,
	}) + "\n");

	private Dictionary<string, string> PsRow(DemoDockerContainer container) => new()
	{
		["Command"] = $"\"{container.Command}\"",
		["CreatedAt"] = ListingTime(container.Created),
		["ID"] = container.Id,
		["Image"] = container.Image,
		["Labels"] = string.Join(',', container.Labels.Select(label => $"{label.Key}={label.Value}")),
		["LocalVolumes"] = container.Volumes.Count.ToString(CultureInfo.InvariantCulture),
		["Mounts"] = string.Join(',', container.Volumes),
		["Names"] = container.Name,
		["Networks"] = string.Join(',', container.Networks),
		["Ports"] = container.IsAlive ? string.Join(", ", container.Ports) : "",
		["RunningFor"] = Ago(_time.GetUtcNow() - container.Created) + " ago",
		["Size"] = "0B",
		["State"] = container.State,
		["Status"] = StatusOf(container),
	};

	private DemoDockerResult Inspect(string name)
	{
		if (Find(name) is not { } container)
		{
			return DemoDockerResult.Fail(1, $"Error response from daemon: No such container: {name}");
		}

		Dictionary<string, object?> ports = [];
		foreach (string port in container.Ports)
		{
			string[] parts = port.Split("->");
			string inside = parts[^1];
			if (parts.Length == 1)
			{
				ports[inside] = null;
				continue;
			}

			int colon = parts[0].LastIndexOf(':');
			string address = parts[0][..colon];
			string hostPort = parts[0][(colon + 1)..];
			List<object> bindings = ports.TryGetValue(inside, out object? existing) && existing is List<object> list ? list : [];
			bindings.Add(new { HostIp = address == ":::" || address == "::" ? "::" : address, HostPort = hostPort });
			ports[inside] = bindings;
		}

		var inspected = new[]
		{
			new
			{
				Id = container.Id,
				Created = container.Created.ToString("o", CultureInfo.InvariantCulture),
				Path = container.Command.Split(' ')[0],
				Name = "/" + container.Name,
				Image = "sha256:" + HexId(container.Image),
				RestartCount = container.RestartPolicy == "no" ? 0 : 2,
				State = new
				{
					Status = container.State,
					Running = container.State == "running",
					Paused = container.State == "paused",
					OOMKilled = false,
					Pid = container.IsAlive ? 4000 + (container.Name.Length * 37) : 0,
					ExitCode = container.ExitCode,
					Error = "",
					StartedAt = container.StartedAt.ToString("o", CultureInfo.InvariantCulture),
					FinishedAt = container.IsAlive ? "0001-01-01T00:00:00Z" : container.FinishedAt.ToString("o", CultureInfo.InvariantCulture),
					Health = container.Health.Length == 0 ? null : new
					{
						Status = container.Health,
						FailingStreak = container.Health == "unhealthy" ? 3 : 0,
						Log = new[]
						{
							new { Start = _time.GetUtcNow().AddSeconds(-30).ToString("o", CultureInfo.InvariantCulture), ExitCode = 0, Output = "OK\n" },
						},
					},
				},
				HostConfig = new
				{
					NetworkMode = container.Networks.Count > 0 ? container.Networks[0] : "default",
					Privileged = false,
					RestartPolicy = new
					{
						Name = container.RestartPolicy.Split(':')[0],
						MaximumRetryCount = container.RestartPolicy.Contains(':', StringComparison.Ordinal) ? int.Parse(container.RestartPolicy.Split(':')[1], CultureInfo.InvariantCulture) : 0,
					},
				},
				Mounts = container.Volumes.Select(volume => new
				{
					Type = "volume",
					Name = volume,
					Source = $"/var/lib/docker/volumes/{volume}/_data",
					Destination = volume.Contains("pg", StringComparison.Ordinal) ? "/var/lib/postgresql/data" : "/var/lib/grafana",
					Driver = "local",
					Mode = "z",
					RW = true,
				}),
				Config = new
				{
					Hostname = container.Id[..12],
					User = container.Name.StartsWith("monitoring", StringComparison.Ordinal) ? "472" : "",
					Env = container.Environment.Append("PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin"),
					Cmd = container.Command.Split(' ').Skip(1),
					Image = container.Image,
					WorkingDir = "",
					Entrypoint = new[] { container.Command.Split(' ')[0] },
					Labels = container.Labels,
				},
				NetworkSettings = new
				{
					Ports = ports,
					Networks = container.Networks.ToDictionary(
						network => network,
						network => new
						{
							IPAddress = container.IsAlive ? $"172.18.0.{2 + container.Name.Length % 10}" : "",
							Gateway = container.IsAlive ? "172.18.0.1" : "",
							MacAddress = "02:42:ac:12:00:02",
							GlobalIPv6Address = "",
							Aliases = container.Service is { } service ? new[] { container.Name, service } : [container.Name],
						}),
				},
			},
		};

		return DemoDockerResult.Ok(JsonSerializer.Serialize(inspected, Indented) + "\n");
	}

	private DemoDockerResult Stats(IEnumerable<string> names)
	{
		StringBuilder output = new();
		double phase = _time.GetUtcNow().ToUnixTimeMilliseconds() / 4000d;
		foreach (string name in names)
		{
			if (Find(name) is not { } container)
			{
				return DemoDockerResult.Fail(1, $"Error response from daemon: No such container: {name}");
			}

			bool running = container.State == "running";
			double cpu = running ? container.CpuBase * (1.4 + Math.Sin(phase + container.Name.Length)) : 0;
			long memory = container.IsAlive ? container.MemoryBytes + (long)(container.MemoryBytes * 0.05 * Math.Sin(phase / 3)) : 0;
			const double Limit = 8_201_457_664d;
			output.Append(Line(new Dictionary<string, string>
			{
				["BlockIO"] = running ? "12.4MB / 3.1MB" : "0B / 0B",
				["CPUPerc"] = string.Create(CultureInfo.InvariantCulture, $"{cpu:0.00}%"),
				["Container"] = container.Id,
				["ID"] = container.Id,
				["MemPerc"] = string.Create(CultureInfo.InvariantCulture, $"{memory * 100 / Limit:0.00}%"),
				["MemUsage"] = string.Create(CultureInfo.InvariantCulture, $"{memory / 1048576d:0.##}MiB / 7.638GiB"),
				["Name"] = container.Name,
				["NetIO"] = running ? "1.85MB / 920kB" : "0B / 0B",
				["PIDs"] = running ? "12" : "0",
			})).Append('\n');
		}

		return DemoDockerResult.Ok(output.ToString());
	}

	/// <summary>
	/// A running container logs a line every two seconds, cycling through its own lines; the API's lines that start with
	/// <c>warn</c> or <c>fail</c> go to stderr, like a real app's would.
	/// </summary>
	private DemoDockerResult Logs(string[] words)
	{
		int tail = 100;
		string? since = null;
		string? name = null;
		for (int index = 0; index < words.Length; index++)
		{
			switch (words[index])
			{
				case "--tail":
					tail = int.Parse(words[++index], CultureInfo.InvariantCulture);
					break;
				case "--since":
					since = words[++index];
					break;
				case "--timestamps":
					break;
				default:
					name = words[index];
					break;
			}
		}

		if (name is null || Find(name) is not { } container)
		{
			return DemoDockerResult.Fail(1, $"Error response from daemon: No such container: {name}");
		}

		DateTimeOffset end = container.IsAlive ? _time.GetUtcNow() : container.FinishedAt;
		long last = end.ToUnixTimeSeconds() / 2;
		long first = Math.Max(last - tail + 1, container.Created.ToUnixTimeSeconds() / 2);
		StringBuilder output = new();
		StringBuilder error = new();
		for (long tick = first; tick <= last; tick++)
		{
			string stamp = DateTimeOffset.FromUnixTimeSeconds(tick * 2).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'.000000000Z'", CultureInfo.InvariantCulture);
			if (since is not null && string.CompareOrdinal(stamp, since) < 0)
			{
				continue;
			}

			string text = container.LogLines[(int)(tick % container.LogLines.Count)];
			bool toError = text.StartsWith("warn", StringComparison.Ordinal) || text.StartsWith("fail", StringComparison.Ordinal);
			(toError ? error : output).Append(stamp).Append(' ').Append(text).Append('\n');
		}

		return new DemoDockerResult(0, output.ToString(), error.ToString());
	}

	private DemoDockerResult FindBash(string name) => Find(name) switch
	{
		null => DemoDockerResult.Fail(1, $"Error response from daemon: No such container: {name}"),
		{ State: not "running" } => DemoDockerResult.Fail(1, $"Error response from daemon: container {name} is not running"),
		{ HasBash: true } => DemoDockerResult.Ok("/usr/bin/bash\n"),
		_ => new DemoDockerResult(1, "", ""),
	};

	private DemoDockerResult Change(string name, Action<DemoDockerContainer> change)
	{
		if (Find(name) is not { } container)
		{
			return DemoDockerResult.Fail(1, $"Error response from daemon: No such container: {name}");
		}

		change(container);
		return DemoDockerResult.Ok(name + "\n");
	}

	private void Start(DemoDockerContainer container)
	{
		container.State = "running";
		container.ExitCode = 0;
		container.StartedAt = _time.GetUtcNow();
		if (container.Health.Length > 0)
		{
			container.Health = "starting";
		}
	}

	private void Stop(DemoDockerContainer container, int exitCode)
	{
		if (container.IsAlive)
		{
			container.State = "exited";
			container.ExitCode = exitCode;
			container.FinishedAt = _time.GetUtcNow();
		}
	}

	private DemoDockerResult Remove(string[] words)
	{
		bool force = words.Contains("--force");
		string name = words[^1];
		if (Find(name) is not { } container)
		{
			return DemoDockerResult.Fail(1, $"Error response from daemon: No such container: {name}");
		}

		if (container.IsAlive && !force)
		{
			return DemoDockerResult.Fail(1, $"Error response from daemon: cannot remove container \"/{name}\": container is running: stop the container before removing or force remove");
		}

		_containers.Remove(container);
		return DemoDockerResult.Ok(name + "\n");
	}

	private Dictionary<string, string> ImageRow((string Repository, string Tag, string Id, long Size, DateTimeOffset Created) image) => new()
	{
		["Containers"] = "N/A",
		["CreatedAt"] = ListingTime(image.Created),
		["CreatedSince"] = Ago(_time.GetUtcNow() - image.Created) + " ago",
		["Digest"] = "<none>",
		["ID"] = image.Id,
		["Repository"] = image.Repository,
		["SharedSize"] = "N/A",
		["Size"] = string.Create(CultureInfo.InvariantCulture, $"{image.Size / 1e6:0.#}MB"),
		["Tag"] = image.Tag,
		["UniqueSize"] = "N/A",
		["VirtualSize"] = string.Create(CultureInfo.InvariantCulture, $"{image.Size / 1e6:0.#}MB"),
	};

	private DemoDockerResult Pull(string reference)
	{
		int colon = reference.LastIndexOf(':');
		bool tagged = colon > reference.LastIndexOf('/');
		string repository = tagged ? reference[..colon] : reference;
		string tag = tagged ? reference[(colon + 1)..] : "latest";
		if (!_images.Any(image => image.Repository == repository && image.Tag == tag))
		{
			_images.Add((repository, tag, "sha256:" + HexId(reference), 31_400_000, _time.GetUtcNow().AddDays(-4)));
		}

		string digest = "sha256:" + HexId("digest" + reference);
		return DemoDockerResult.Ok(
			$"{tag}: Pulling from {repository}\n6e771e15690e: Pull complete\n0b2a6a6e5c1f: Pull complete\nDigest: {digest}\nStatus: Downloaded newer image for {repository}:{tag}\ndocker.io/{repository}:{tag}\n");
	}

	private DemoDockerResult RemoveImage(string[] words)
	{
		bool force = words.Contains("--force");
		string reference = words[^1];
		int index = _images.FindIndex(image => image.Id == reference || $"{image.Repository}:{image.Tag}" == reference);
		if (index < 0)
		{
			return DemoDockerResult.Fail(1, $"Error response from daemon: No such image: {reference}");
		}

		(string repository, string tag, string id, _, _) = _images[index];
		string name = $"{repository}:{tag}";
		if (!force && _containers.FirstOrDefault(container => container.Image == name) is { } user)
		{
			return DemoDockerResult.Fail(1, $"Error response from daemon: conflict: unable to remove repository reference \"{name}\" (must force) - container {user.Id[..12]} is using its referenced image {id[7..19]}");
		}

		_images.RemoveAt(index);
		return DemoDockerResult.Ok(repository == "<none>" ? $"Deleted: {id}\n" : $"Untagged: {name}\nDeleted: {id}\n");
	}

	private DemoDockerResult PruneImages()
	{
		List<string> removed = [.. _images.Where(image => image.Repository == "<none>").Select(image => image.Id)];
		_images.RemoveAll(image => image.Repository == "<none>");
		return DemoDockerResult.Ok(removed.Count == 0
			? "Total reclaimed space: 0B\n"
			: "Deleted Images:\n" + string.Concat(removed.Select(id => $"deleted: {id}\n")) + "\nTotal reclaimed space: 198MB\n");
	}

	private bool VolumeInUse(string volume) => _containers.Any(container => container.Volumes.Contains(volume));

	private static Dictionary<string, string> VolumeRow(string volume) => new()
	{
		["Availability"] = "N/A",
		["Driver"] = "local",
		["Group"] = "N/A",
		["Labels"] = "",
		["Links"] = "N/A",
		["Mountpoint"] = $"/var/lib/docker/volumes/{volume}/_data",
		["Name"] = volume,
		["Scope"] = "local",
		["Size"] = "N/A",
		["Status"] = "N/A",
	};

	private DemoDockerResult CreateVolume(string name)
	{
		if (!_volumes.Contains(name))
		{
			_volumes.Add(name);
		}

		return DemoDockerResult.Ok(name + "\n");
	}

	private DemoDockerResult RemoveVolumes(string[] names)
	{
		StringBuilder output = new();
		foreach (string name in names)
		{
			if (VolumeInUse(name))
			{
				return DemoDockerResult.Fail(1, $"Error response from daemon: remove {name}: volume is in use");
			}

			_volumes.Remove(name);
			output.Append(name).Append('\n');
		}

		return DemoDockerResult.Ok(output.ToString());
	}

	private Dictionary<string, string> NetworkRow((string Name, string Id, string Driver) network) => new()
	{
		["CreatedAt"] = ListingTime(_time.GetUtcNow().AddDays(-40)),
		["Driver"] = network.Driver,
		["ID"] = network.Id,
		["IPv4"] = "true",
		["IPv6"] = "false",
		["Internal"] = "false",
		["Labels"] = "",
		["Name"] = network.Name,
		["Scope"] = "local",
	};

	private DemoDockerResult CreateNetwork(string name)
	{
		if (_networks.Any(network => network.Name == name))
		{
			return DemoDockerResult.Fail(1, $"Error response from daemon: network with name {name} already exists");
		}

		string id = HexId("network-" + name);
		_networks.Add((name, id, "bridge"));
		return DemoDockerResult.Ok(id + "\n");
	}

	private DemoDockerResult RemoveNetworks(string[] names)
	{
		StringBuilder output = new();
		foreach (string name in names)
		{
			int index = _networks.FindIndex(network => network.Name == name || network.Id == name);
			if (index < 0)
			{
				return DemoDockerResult.Fail(1, $"Error response from daemon: network {name} not found");
			}

			_networks.RemoveAt(index);
			output.Append(name).Append('\n');
		}

		return DemoDockerResult.Ok(output.ToString());
	}

	private DemoDockerResult DiskUsage()
	{
		long images = _images.Sum(image => image.Size);
		long dangling = _images.Where(image => image.Repository == "<none>").Sum(image => image.Size);
		string Rows(params (string Type, int Total, int Active, string Size, string Reclaimable)[] rows) =>
			string.Concat(rows.Select(row => Line(new Dictionary<string, string>
			{
				["Active"] = row.Active.ToString(CultureInfo.InvariantCulture),
				["Reclaimable"] = row.Reclaimable,
				["Size"] = row.Size,
				["TotalCount"] = row.Total.ToString(CultureInfo.InvariantCulture),
				["Type"] = row.Type,
			}) + "\n"));

		return DemoDockerResult.Ok(Rows(
			("Images", _images.Count, _containers.Select(container => container.Image).Distinct().Count(), string.Create(CultureInfo.InvariantCulture, $"{images / 1e9:0.##}GB"), string.Create(CultureInfo.InvariantCulture, $"{(dangling + 4_260_000) / 1e6:0.#}MB ({(dangling + 4_260_000) * 100 / Math.Max(images, 1)}%)")),
			("Containers", _containers.Count, _containers.Count(container => container.IsAlive), "1.2MB", "18kB (1%)"),
			("Local Volumes", _volumes.Count, _volumes.Count(VolumeInUse), "1.43GB", "212MB (14%)"),
			("Build Cache", 31, 0, "642MB", "642MB")));
	}

	private DemoDockerResult SystemPrune()
	{
		int containers = _containers.RemoveAll(container => !container.IsAlive);
		int networks = _networks.RemoveAll(network => network.Name is not ("bridge" or "host" or "none") && !_containers.Any(container => container.Networks.Contains(network.Name)));
		int images = _images.RemoveAll(image => image.Repository == "<none>");
		return DemoDockerResult.Ok(string.Create(
			CultureInfo.InvariantCulture,
			$"Deleted Containers: {containers}\nDeleted Networks: {networks}\nDeleted Images: {images}\nDeleted build cache objects: 31\n\nTotal reclaimed space: 840.2MB\n"));
	}
}
