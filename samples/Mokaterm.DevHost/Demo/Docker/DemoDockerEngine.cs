using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Mokaterm.DevHost.Demo.Docker;

/// <summary>
/// A Docker engine that lives in memory, so the Docker manager can be looked at without a docker host. It answers the
/// commands the manager sends with the JSON docker prints, and its containers start, stop and go away when told to.
/// Two compose projects (<c>shop</c> and <c>monitoring</c>), a few loose containers, a dangling image, unused volumes and
/// an unused network give every page something to show.
/// </summary>
internal sealed partial class DemoDockerEngine
{
	private const string ShopFolder = "/srv/shop";
	private const string ShopFile = "/srv/shop/compose.yaml";
	private const string MonitoringFolder = "/opt/monitoring";

	private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

	private readonly Lock _lock = new();
	private readonly TimeProvider _time;
	private readonly Func<string, string?> _readFile;
	private readonly Func<string, byte[]?> _readBytes;
	private readonly List<DemoDockerContainer> _containers = [];
	private readonly List<(string Repository, string Tag, string Id, long Size, DateTimeOffset Created)> _images = [];
	private readonly List<string> _volumes = [];
	private readonly List<(string Name, string Id, string Driver)> _networks = [];

	/// <param name="readFile">The text of a file on the demo host, or null when there is none.</param>
	/// <param name="readBytes">The bytes of a file on the demo host, for one piped into a container; null when the demo has no host files.</param>
	public DemoDockerEngine(TimeProvider time, Func<string, string?> readFile, Func<string, byte[]?>? readBytes = null)
	{
		_time = time;
		_readFile = readFile;
		_readBytes = readBytes ?? (_ => null);
		DateTimeOffset now = time.GetUtcNow();
		SeedImages(now);
		_volumes.AddRange(["shop_pgdata", "monitoring_grafana", "old-cache", HexId("anon-1"), HexId("anon-2")]);
		_networks.AddRange([("bridge", HexId("bridge"), "bridge"), ("host", HexId("host"), "host"), ("none", HexId("none"), "null"), ("legacy_net", HexId("legacy"), "bridge")]);
		AddShop(now);
		AddMonitoring(now);
		_containers.Add(new DemoDockerContainer
		{
			Id = HexId("redis"),
			Name = "redis",
			Image = "redis:7.4",
			Command = "docker-entrypoint.sh redis-server",
			Created = now.AddDays(-12),
			State = "exited",
			StartedAt = now.AddDays(-12),
			FinishedAt = now.AddDays(-3),
			LogLines = ["Ready to accept connections tcp", "DB saved on disk", "Background saving terminated with success"],
			Networks = ["bridge"],
			Environment = ["REDIS_ARGS=--save 60 1"],
		});
		_containers.Add(new DemoDockerContainer
		{
			Id = HexId("backup"),
			Name = "backup-cron",
			Image = "alpine:3.20",
			Command = "/bin/sh -c 'crond -f'",
			Created = now.AddDays(-30),
			State = "exited",
			ExitCode = 1,
			StartedAt = now.AddHours(-26),
			FinishedAt = now.AddHours(-25),
			RestartPolicy = "on-failure:3",
			HasBash = false,
			LogLines = ["crond: crond (busybox 1.36.1) started, log level 8", "backup: uploading /data/dump.sql.gz", "backup: upload failed: connection refused"],
			Networks = ["bridge"],
			Environment = ["S3_BUCKET=acme-backups", "S3_SECRET_KEY=wJalrXUtnFEMI/K7MDENG/bPxRfiCY"],
		});
		SeedOperations(now);
	}

	/// <summary>Runs one docker command line and answers the way docker would.</summary>
	public DemoDockerResult Run(string[] words, string? directory)
	{
		ArgumentNullException.ThrowIfNull(words);
		lock (_lock)
		{
			Advance();
			return words switch
			{
				["sh", "-c", var script, "sh", .. var arguments] => Script(script, arguments),
				["docker-compose", ..] => DemoDockerResult.Fail(127, "sh: 1: docker-compose: not found"),
				["docker", "compose", .. var rest] => Compose(rest, directory),
				["docker", .. var rest] => Docker(rest),
				_ => DemoDockerResult.Fail(127, $"sh: 1: {(words.Length > 0 ? words[0] : "")}: not found"),
			};
		}
	}

	private static string HexId(string seed) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed)));

	private static string Line(object value) => JsonSerializer.Serialize(value, Json);

	private static string ListingTime(DateTimeOffset time) =>
		time.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss '+0000 UTC'", CultureInfo.InvariantCulture);

	private static string Ago(TimeSpan age) => age.TotalSeconds switch
	{
		< 2 => "Less than a second",
		< 60 => string.Create(CultureInfo.InvariantCulture, $"{(int)age.TotalSeconds} seconds"),
		< 120 => "About a minute",
		< 3600 => string.Create(CultureInfo.InvariantCulture, $"{(int)age.TotalMinutes} minutes"),
		< 7200 => "About an hour",
		< 172_800 => string.Create(CultureInfo.InvariantCulture, $"{(int)age.TotalHours} hours"),
		_ => string.Create(CultureInfo.InvariantCulture, $"{(int)age.TotalDays} days"),
	};

	private void SeedImages(DateTimeOffset now)
	{
		_images.Add(("nginx", "1.27-alpine", "sha256:" + HexId("nginx"), 48_300_000, now.AddDays(-20)));
		_images.Add(("ghcr.io/acme/shop-api", "2.4.1", "sha256:" + HexId("shop-api"), 212_000_000, now.AddDays(-2)));
		_images.Add(("postgres", "16", "sha256:" + HexId("postgres"), 432_000_000, now.AddDays(-40)));
		_images.Add(("redis", "7.4", "sha256:" + HexId("redis-image"), 117_000_000, now.AddDays(-35)));
		_images.Add(("alpine", "3.20", "sha256:" + HexId("alpine"), 7_800_000, now.AddDays(-60)));
		_images.Add(("grafana/grafana", "11.2.0", "sha256:" + HexId("grafana"), 474_000_000, now.AddDays(-15)));
		_images.Add(("busybox", "latest", "sha256:" + HexId("busybox"), 4_260_000, now.AddDays(-90)));
		_images.Add(("<none>", "<none>", "sha256:" + HexId("dangling"), 198_000_000, now.AddDays(-9)));
	}

	private void AddShop(DateTimeOffset now)
	{
		Dictionary<string, string> Labels(string service) => new()
		{
			["com.docker.compose.project"] = "shop",
			["com.docker.compose.service"] = service,
			["com.docker.compose.project.working_dir"] = ShopFolder,
			["com.docker.compose.project.config_files"] = ShopFile,
			["com.docker.compose.oneoff"] = "False",
		};

		if (!_networks.Any(network => network.Name == "shop_default"))
		{
			_networks.Add(("shop_default", HexId("shop_default"), "bridge"));
		}

		_containers.Add(new DemoDockerContainer
		{
			Id = HexId("shop-web-" + now.Ticks),
			Name = "shop-web-1",
			Image = "nginx:1.27-alpine",
			Command = "/docker-entrypoint.sh nginx -g 'daemon off;'",
			Created = now.AddHours(-50),
			StartedAt = now.AddHours(-50),
			Health = "healthy",
			Ports = ["0.0.0.0:8080->80/tcp", ":::8080->80/tcp"],
			Labels = Labels("web"),
			Networks = ["shop_default"],
			Environment = ["NGINX_PORT=80", "NGINX_ENTRYPOINT_QUIET_LOGS=1"],
			LogLines =
			[
				"10.0.0.7 - - \"GET / HTTP/1.1\" 200 615 \"-\" \"Mozilla/5.0\"",
				"10.0.0.7 - - \"GET /api/cart HTTP/1.1\" 200 81 \"-\" \"Mozilla/5.0\"",
				"10.0.0.9 - - \"GET /favicon.ico HTTP/1.1\" 404 153 \"-\" \"curl/8.5.0\"",
			],
			HasBash = false,
			CpuBase = 0.4,
			MemoryBytes = 9L * 1024 * 1024,
		});
		_containers.Add(new DemoDockerContainer
		{
			Id = HexId("shop-api-" + now.Ticks),
			Name = "shop-api-1",
			Image = "ghcr.io/acme/shop-api:2.4.1",
			Command = "dotnet Shop.Api.dll",
			Created = now.AddHours(-50),
			StartedAt = now.AddMinutes(-7),
			Health = "starting",
			Ports = ["127.0.0.1:5000->5000/tcp"],
			Labels = Labels("api"),
			Networks = ["shop_default"],
			Environment = ["ASPNETCORE_URLS=http://+:5000", "ConnectionStrings__Shop=Host=db;Username=shop;Password=s3cr3t-demo", "API_TOKEN=tok_live_demo_9f8e7d"],
			LogLines =
			[
				"info: Microsoft.Hosting.Lifetime[14] Now listening on: http://[::]:5000",
				"info: Shop.Api.Orders[0] Order 18231 placed",
				"warn: Shop.Api.Payments[3] Payment provider answered slowly (1840 ms)",
				"fail: Shop.Api.Payments[7] Payment provider timed out after 5000 ms",
			],
			CpuBase = 6,
			MemoryBytes = 186L * 1024 * 1024,
		});
		_containers.Add(new DemoDockerContainer
		{
			Id = HexId("shop-db-" + now.Ticks),
			Name = "shop-db-1",
			Image = "postgres:16",
			Command = "docker-entrypoint.sh postgres",
			Created = now.AddHours(-50),
			StartedAt = now.AddHours(-50),
			Health = "healthy",
			Ports = ["5432/tcp"],
			Labels = Labels("db"),
			Networks = ["shop_default"],
			Volumes = ["shop_pgdata"],
			Environment = ["POSTGRES_USER=shop", "POSTGRES_PASSWORD=s3cr3t-demo", "POSTGRES_DB=shop", "PGDATA=/var/lib/postgresql/data"],
			LogLines =
			[
				"LOG:  checkpoint starting: time",
				"LOG:  checkpoint complete: wrote 42 buffers (0.3%)",
				"LOG:  database system is ready to accept connections",
			],
			CpuBase = 2.5,
			MemoryBytes = 96L * 1024 * 1024,
		});
	}

	private void AddMonitoring(DateTimeOffset now)
	{
		_containers.Add(new DemoDockerContainer
		{
			Id = HexId("grafana-" + now.Ticks),
			Name = "monitoring-grafana-1",
			Image = "grafana/grafana:11.2.0",
			Command = "/run.sh",
			Created = now.AddDays(-15),
			StartedAt = now.AddDays(-15),
			State = "paused",
			Ports = ["0.0.0.0:3000->3000/tcp"],
			Labels = new Dictionary<string, string>
			{
				["com.docker.compose.project"] = "monitoring",
				["com.docker.compose.service"] = "grafana",
				["com.docker.compose.project.working_dir"] = MonitoringFolder,
				["com.docker.compose.project.config_files"] = $"{MonitoringFolder}/docker-compose.yml,{MonitoringFolder}/docker-compose.override.yml",
			},
			Volumes = ["monitoring_grafana"],
			Networks = ["bridge"],
			Environment = ["GF_SECURITY_ADMIN_PASSWORD=admin-demo", "GF_SERVER_ROOT_URL=https://grafana.example.com"],
			LogLines = ["logger=http.server t=info msg=\"HTTP Server Listen\" address=[::]:3000", "logger=cleanup t=info msg=\"Completed cleanup jobs\""],
		});
	}

	private DemoDockerContainer? Find(string reference) =>
		_containers.FirstOrDefault(container => container.Name == reference || container.Id.StartsWith(reference, StringComparison.Ordinal));

	private string StatusOf(DemoDockerContainer container)
	{
		DateTimeOffset now = _time.GetUtcNow();
		string health = container.Health.Length == 0 ? "" : container.Health == "starting" ? " (health: starting)" : $" ({container.Health})";
		return container.State switch
		{
			"running" => $"Up {Ago(now - container.StartedAt)}{health}",
			"paused" => $"Up {Ago(now - container.StartedAt)} (Paused)",
			"created" => "Created",
			_ => string.Create(CultureInfo.InvariantCulture, $"Exited ({container.ExitCode}) {Ago(now - container.FinishedAt)} ago"),
		};
	}
}
