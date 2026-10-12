using System.Globalization;
using System.Text;

namespace Mokaterm.DevHost.Demo.Docker;

/// <summary>
/// The Docker manager's operations: registry digests for the update check, the verbose disk usage and the cleanups,
/// <c>docker run</c>, and the per-container states the health watch reads. Three containers misbehave on a clock so the
/// watch has something to say: shop-api-1's health check fails for a while every few minutes, queue-worker crashes
/// and is restarted by its policy, and nightly-report exits with an error a while after it starts.
/// </summary>
internal sealed partial class DemoDockerEngine
{
	private const string QueueWorker = "queue-worker";
	private const string NightlyReport = "nightly-report";
	private const string ShopApi = "shop-api-1";
	private const string BuildxVersion = "github.com/docker/buildx v0.17.1 257815a6fb95af2ad6b5383ec5b2bd6b0ed00dbd";

	private static readonly TimeSpan CrashEvery = TimeSpan.FromSeconds(70);
	private static readonly TimeSpan ReportRunsFor = TimeSpan.FromSeconds(100);

	private static readonly Dictionary<string, long> VolumeSizes = new(StringComparer.Ordinal)
	{
		["shop_pgdata"] = 412_600_000,
		["monitoring_grafana"] = 38_200_000,
		["old-cache"] = 212_000_000,
	};

	/// <summary>The digest each tag was pulled as, which <c>image inspect</c> reports in RepoDigests. A tag missing here was built on the host.</summary>
	private readonly Dictionary<string, string> _pulled = new(StringComparer.Ordinal);

	/// <summary>What the registry holds for a tag now, or why it will not say.</summary>
	private readonly Dictionary<string, string> _registry = new(StringComparer.Ordinal);

	private readonly Dictionary<string, string> _registryErrors = new(StringComparer.Ordinal);

	private readonly List<(string Id, string Type, string Description, long Size, bool InUse, DateTimeOffset LastUsed)> _buildCache = [];

	private static string Digest(string seed) => "sha256:" + HexId(seed);

	/// <summary><c>nginx</c> for Docker Hub's library, the repository as written otherwise: what a RepoDigests entry starts with.</summary>
	private static string RepositoryOf(string tagged) => tagged[..tagged.LastIndexOf(':')];

	private static string Tagged(string reference)
	{
		int colon = reference.LastIndexOf(':');
		return colon > reference.LastIndexOf('/') ? reference : reference + ":latest";
	}

	private static string FullName(string tagged)
	{
		string repository = RepositoryOf(tagged);
		string first = repository.Split('/')[0];
		bool registry = first.Contains('.', StringComparison.Ordinal) || first.Contains(':', StringComparison.Ordinal) || first == "localhost";
		string full = registry ? repository : repository.Contains('/', StringComparison.Ordinal) ? "docker.io/" + repository : "docker.io/library/" + repository;
		return full + tagged[tagged.LastIndexOf(':')..];
	}

	private void SeedOperations(DateTimeOffset now)
	{
		_images.Add(("registry.example.com/ops/queue-worker", "1.8", "sha256:" + HexId("queue-worker-image"), 64_000_000, now.AddDays(-4)));
		_images.Add(("shop-worker", "dev", "sha256:" + HexId("shop-worker-dev"), 236_000_000, now.AddHours(-30)));
		foreach ((string repository, string tag, _, _, _) in _images.Where(image => image.Repository != "<none>"))
		{
			string tagged = $"{repository}:{tag}";
			if (repository.StartsWith("shop-worker", StringComparison.Ordinal))
			{
				continue;
			}

			_pulled[tagged] = Digest("pulled " + tagged);
			_registry[tagged] = tagged is "nginx:1.27-alpine" or "postgres:16" ? Digest("newer " + tagged) : _pulled[tagged];
		}

		_registryErrors["ghcr.io/acme/shop-api:2.4.1"] = "ERROR: unauthorized: authentication required";
		_registryErrors["registry.example.com/ops/queue-worker:1.8"] =
			"ERROR: failed to do request: Head \"https://registry.example.com/v2/ops/queue-worker/manifests/1.8\": dial tcp: lookup registry.example.com on 127.0.0.53:53: no such host";

		_buildCache.AddRange(
		[
			(HexId("cache-1"), "regular", "[build 3/6] RUN apt-get update && apt-get install -y curl", 182_000_000, false, now.AddDays(-6)),
			(HexId("cache-2"), "regular", "[build 4/6] RUN dotnet restore", 311_000_000, false, now.AddDays(-2)),
			(HexId("cache-3"), "source.local", "local source for context", 41_000_000, false, now.AddDays(-2)),
			(HexId("cache-4"), "exec.cachemount", "cached mount /root/.nuget/packages from exec /bin/sh -c dotnet restore", 108_000_000, false, now.AddDays(-2)),
			(HexId("cache-5"), "regular", "[final 2/2] COPY --from=build /app /app", 9_400_000, true, now.AddHours(-1)),
		]);

		_containers.Add(new DemoDockerContainer
		{
			Id = HexId("queue-worker"),
			Name = QueueWorker,
			Image = "registry.example.com/ops/queue-worker:1.8",
			Command = "/usr/local/bin/worker --queue jobs",
			Created = now.AddDays(-4),
			StartedAt = now,
			RestartPolicy = "on-failure:10",
			RestartCount = 3,
			Networks = ["bridge"],
			Environment = ["QUEUE_URL=amqp://jobs:demo-pass@rabbit:5672/"],
			LogLines = ["worker: connected to the queue", "worker: picked job 4412", "panic: runtime error: invalid memory address or nil pointer dereference"],
			HasBash = false,
			CpuBase = 0.8,
			MemoryBytes = 22L * 1024 * 1024,
		});
		_containers.Add(new DemoDockerContainer
		{
			Id = HexId("nightly-report"),
			Name = NightlyReport,
			Image = "alpine:3.20",
			Command = "/bin/sh /jobs/report.sh",
			Created = now.AddDays(-1),
			StartedAt = now,
			Networks = ["bridge"],
			LogLines = ["report: collecting yesterday's orders", "report: rendering 18 pages", "report: upload failed: upstream timed out"],
			HasBash = false,
			CpuBase = 3,
		});
	}

	/// <summary>Moves the misbehaving containers along to now, before every command.</summary>
	private void Advance()
	{
		DateTimeOffset now = _time.GetUtcNow();
		foreach (DemoDockerContainer container in _containers.Where(container => container.State == "running"))
		{
			TimeSpan up = now - container.StartedAt;
			switch (container.Name)
			{
				case ShopApi when container.Health.Length > 0:
					container.Health = up < TimeSpan.FromSeconds(25) ? "starting"
						: (up.TotalSeconds - 25) % 180 < 140 ? "healthy" : "unhealthy";
					break;
				case QueueWorker when up >= CrashEvery && container.RestartCount < 10:
					container.RestartCount++;
					container.StartedAt = now;
					break;
				case QueueWorker when up >= CrashEvery:
				case NightlyReport when up >= ReportRunsFor:
					container.State = "exited";
					container.ExitCode = 1;
					container.FinishedAt = now;
					break;
			}
		}
	}

	/// <summary>The template the health watch sends is fixed, so the demo answers its fields without reading it.</summary>
	private DemoDockerResult ContainerStates() => DemoDockerResult.Ok(string.Concat(_containers.Select(container => string.Create(
		CultureInfo.InvariantCulture,
		$"{container.Id} {container.RestartCount} {container.State} {container.ExitCode} {(container.Health.Length > 0 ? container.Health : "-")} /{container.Name} {container.Project}\n"))));

	private DemoDockerResult InspectImages(string[] references)
	{
		List<object> found = [];
		StringBuilder error = new();
		foreach (string reference in references)
		{
			string tagged = Tagged(reference);
			int index = _images.FindIndex(image => $"{image.Repository}:{image.Tag}" == tagged || image.Id == reference);
			if (index < 0)
			{
				error.Append(CultureInfo.InvariantCulture, $"Error response from daemon: No such image: {reference}\n");
				continue;
			}

			(string repository, string tag, string id, long size, DateTimeOffset created) = _images[index];
			string key = $"{repository}:{tag}";
			found.Add(new
			{
				Id = id,
				RepoTags = repository == "<none>" ? Array.Empty<string>() : [key],
				RepoDigests = _pulled.TryGetValue(key, out string? digest) ? new[] { $"{repository}@{digest}" } : [],
				Created = created.ToString("o", CultureInfo.InvariantCulture),
				Size = size,
				Os = "linux",
				Architecture = "amd64",
			});
		}

		return new DemoDockerResult(error.Length == 0 ? 0 : 1, System.Text.Json.JsonSerializer.Serialize(found, Indented) + "\n", error.ToString());
	}

	private DemoDockerResult Imagetools(string reference)
	{
		string tagged = Tagged(reference);
		if (_registryErrors.TryGetValue(tagged, out string? refused))
		{
			return DemoDockerResult.Fail(1, refused);
		}

		if (!_registry.TryGetValue(tagged, out string? digest))
		{
			return DemoDockerResult.Fail(1, $"ERROR: {FullName(tagged)}: not found");
		}

		string name = FullName(tagged);
		return DemoDockerResult.Ok(
			$"Name:      {name}\nMediaType: application/vnd.oci.image.index.v1+json\nDigest:    {digest}\n\nManifests:\n"
			+ $"  Name:        {name}@{Digest(digest + "amd64")}\n  MediaType:   application/vnd.oci.image.manifest.v1+json\n  Platform:    linux/amd64\n\n"
			+ $"  Name:        {name}@{Digest(digest + "arm64")}\n  MediaType:   application/vnd.oci.image.manifest.v1+json\n  Platform:    linux/arm64\n");
	}

	/// <summary>The demo has buildx, so the manager never asks this; it answers for a single-platform manifest all the same.</summary>
	private DemoDockerResult ManifestInspect(string reference)
	{
		string tagged = Tagged(reference);
		if (_registryErrors.TryGetValue(tagged, out string? refused))
		{
			return DemoDockerResult.Fail(1, refused);
		}

		return _registry.TryGetValue(tagged, out string? digest)
			? DemoDockerResult.Ok(System.Text.Json.JsonSerializer.Serialize(
				new
				{
					Ref = FullName(tagged),
					Descriptor = new { mediaType = "application/vnd.docker.distribution.manifest.v2+json", digest, size = 1576, platform = new { architecture = "amd64", os = "linux" } },
					SchemaV2Manifest = new { schemaVersion = 2, config = new { digest = Digest("config " + digest) } },
				},
				Indented) + "\n")
			: DemoDockerResult.Fail(1, $"no such manifest: {FullName(tagged)}");
	}

	/// <summary>Moves a tag to what the registry holds now; the image it left behind stays as a dangling one, as docker leaves it.</summary>
	private bool PullLatest(string reference)
	{
		string tagged = Tagged(reference);
		if (!_registry.TryGetValue(tagged, out string? digest) || (_pulled.TryGetValue(tagged, out string? had) && had == digest))
		{
			return false;
		}

		int index = _images.FindIndex(image => $"{image.Repository}:{image.Tag}" == tagged);
		if (index >= 0)
		{
			(string repository, string tag, string id, long size, DateTimeOffset _) = _images[index];
			_images.Add(("<none>", "<none>", id, size, _time.GetUtcNow().AddDays(-30)));
			_images[index] = (repository, tag, "sha256:" + HexId(tagged + digest), size + 1_200_000, _time.GetUtcNow().AddHours(-6));
		}

		_pulled[tagged] = digest;
		return true;
	}

	/// <summary><c>compose pull</c>: each service's tag moves to what the registry holds, as a real pull would.</summary>
	private DemoDockerResult ComposePull(List<DemoDockerContainer> chosen)
	{
		StringBuilder error = new();
		foreach (DemoDockerContainer container in chosen)
		{
			PullLatest(container.Image);
			error.Append(CultureInfo.InvariantCulture, $" {container.Service} Pulled \n");
		}

		return new DemoDockerResult(0, "", error.ToString());
	}

	private DemoDockerResult SystemUsage()
	{
		Dictionary<string, object> usage = new()
		{
			["Images"] = _images.Select(image =>
			{
				Dictionary<string, string> row = ImageRow(image);
				string tagged = $"{image.Repository}:{image.Tag}";
				row["Containers"] = _containers.Count(container => container.Image == tagged).ToString(CultureInfo.InvariantCulture);
				row["UniqueSize"] = row["Size"];
				row["SharedSize"] = "0B";
				return row;
			}),
			["Containers"] = _containers.Select(container =>
			{
				Dictionary<string, string> row = PsRow(container);
				row["Size"] = container.IsAlive ? "1.09kB" : "18.4kB";
				return row;
			}),
			["Volumes"] = _volumes.Select(volume =>
			{
				Dictionary<string, string> row = VolumeRow(volume);
				row["Links"] = _containers.Count(container => container.Volumes.Contains(volume)).ToString(CultureInfo.InvariantCulture);
				row["Size"] = VolumeSize(volume);
				row["Labels"] = volume.Length == 64 ? "com.docker.volume.anonymous=" : "";
				return row;
			}),
			["BuildCache"] = _buildCache.Select(entry => new Dictionary<string, string>
			{
				["CacheType"] = entry.Type,
				["CreatedSince"] = Ago(_time.GetUtcNow() - entry.LastUsed) + " ago",
				["Description"] = entry.Description,
				["ID"] = entry.Id,
				["InUse"] = entry.InUse ? "true" : "false",
				["LastUsedSince"] = Ago(_time.GetUtcNow() - entry.LastUsed) + " ago",
				["Shared"] = "false",
				["Size"] = Megabytes(entry.Size),
				["UsageCount"] = "2",
			}),
		};

		return DemoDockerResult.Ok(Line(usage) + "\n");
	}

	private DemoDockerResult PruneBuildCache()
	{
		List<string> removed = [.. _buildCache.Where(entry => !entry.InUse).Select(entry => entry.Id)];
		long freed = _buildCache.Where(entry => !entry.InUse).Sum(entry => entry.Size);
		_buildCache.RemoveAll(entry => !entry.InUse);
		return DemoDockerResult.Ok(removed.Count == 0
			? "Total:\t0B\n"
			: "ID\t\t\t\t\t\tRECLAIMABLE\tSIZE\n" + string.Concat(removed.Select(id => id[..25] + "\ttrue\n")) + $"Total:\t{Megabytes(freed)}\n");
	}

	/// <summary>
	/// <c>docker run --detach [--name N] [--restart P] [--network NET] [--publish S]... [--volume S]... [--env K=V]... IMAGE
	/// [CMD...]</c>, the words the manager's form builds. A host port another container holds leaves the new one created
	/// and not started, with docker's own message.
	/// </summary>
	private DemoDockerResult RunContainer(string[] words)
	{
		string? name = null;
		string restart = "no";
		string network = "bridge";
		List<string> ports = [];
		List<string> mounts = [];
		List<string> environment = [];
		int index = 0;
		while (index < words.Length && words[index].StartsWith('-'))
		{
			string flag = words[index];
			if (flag == "--detach")
			{
				index++;
				continue;
			}

			string value = index + 1 < words.Length ? words[index + 1] : "";
			switch (flag)
			{
				case "--name":
					name = value;
					break;
				case "--restart":
					restart = value;
					break;
				case "--network":
					network = value;
					break;
				case "--publish":
					ports.Add(value);
					break;
				case "--volume":
					mounts.Add(value);
					break;
				case "--env":
					environment.Add(value);
					break;
			}

			index += 2;
		}

		if (index >= words.Length)
		{
			return DemoDockerResult.Fail(125, "\"docker run\" requires at least 1 argument.");
		}

		string image = words[index];
		string command = string.Join(' ', words[(index + 1)..]);
		name ??= "demo_" + HexId(image + _time.GetUtcNow().Ticks)[..8];
		if (_containers.FirstOrDefault(container => container.Name == name) is { } taken)
		{
			return DemoDockerResult.Fail(125, $"docker: Error response from daemon: Conflict. The container name \"/{name}\" is already in use by container \"{taken.Id}\". You have to remove (or rename) that container to be able to reuse that name.");
		}

		if (!_networks.Any(known => known.Name == network))
		{
			return DemoDockerResult.Fail(125, $"docker: Error response from daemon: network {network} not found.");
		}

		StringBuilder pulled = new();
		if (!_images.Any(known => $"{known.Repository}:{known.Tag}" == Tagged(image)))
		{
			pulled.Append(CultureInfo.InvariantCulture, $"Unable to find image '{Tagged(image)}' locally\n");
			pulled.Append(Pull(image).Output);
		}

		foreach (string mount in mounts)
		{
			string source = mount.Split(':')[0];
			if (mount.Contains(':', StringComparison.Ordinal) && !source.StartsWith('/') && !_volumes.Contains(source))
			{
				_volumes.Add(source);
			}
		}

		DateTimeOffset now = _time.GetUtcNow();
		DemoDockerContainer created = new()
		{
			Id = HexId("run " + name + now.Ticks),
			Name = name,
			Image = image,
			Command = command.Length > 0 ? command : "/docker-entrypoint.sh",
			Created = now,
			StartedAt = now,
			RestartPolicy = restart,
			Ports = [.. ports.Select(Published)],
			Volumes = [.. mounts.Where(mount => mount.Contains(':', StringComparison.Ordinal) && !mount.StartsWith('/')).Select(mount => mount.Split(':')[0])],
			Networks = [network],
			Environment = environment,
			LogLines = [$"{name}: started from {image}", $"{name}: ready"],
		};
		_containers.Add(created);

		if (HostPortTaken(created) is { } port)
		{
			created.State = "created";
			return new DemoDockerResult(125, created.Id + "\n", pulled + $"docker: Error response from daemon: driver failed programming external connectivity on endpoint {name}: Bind for 0.0.0.0:{port} failed: port is already allocated.\n");
		}

		return new DemoDockerResult(0, created.Id + "\n", pulled.ToString());
	}

	/// <summary><c>8080:80</c> as <c>docker ps</c> prints it: <c>0.0.0.0:8080-&gt;80/tcp</c>.</summary>
	private static string Published(string mapping)
	{
		string protocol = mapping.Contains('/', StringComparison.Ordinal) ? mapping[(mapping.LastIndexOf('/') + 1)..] : "tcp";
		string spec = mapping.Contains('/', StringComparison.Ordinal) ? mapping[..mapping.LastIndexOf('/')] : mapping;
		int last = spec.LastIndexOf(':');
		string inside = spec[(last + 1)..];
		string outside = spec[..last];
		return (outside.Contains(':', StringComparison.Ordinal) ? outside : "0.0.0.0:" + outside) + $"->{inside}/{protocol}";
	}

	private string? HostPortTaken(DemoDockerContainer created)
	{
		HashSet<string> held = [.. _containers
			.Where(container => container != created && container.IsAlive)
			.SelectMany(container => container.Ports)
			.Where(port => port.Contains("->", StringComparison.Ordinal))
			.Select(port => port.Split("->")[0].Split(':')[^1])];
		return created.Ports.Select(port => port.Split("->")[0].Split(':')[^1]).FirstOrDefault(held.Contains);
	}

	private static long VolumeBytes(string volume) =>
		VolumeSizes.TryGetValue(volume, out long size) ? size : volume.Length == 64 ? 1_200_000 : 0;

	private static string VolumeSize(string volume) => Megabytes(VolumeBytes(volume));

	/// <summary>Bytes the way docker prints a size: <c>0B</c>, <c>212MB</c>, <c>1.43GB</c>.</summary>
	private static string Megabytes(long bytes) => bytes switch
	{
		0 => "0B",
		>= 1_000_000_000 => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1e9:0.##}GB"),
		_ => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1e6:0.#}MB"),
	};
}
