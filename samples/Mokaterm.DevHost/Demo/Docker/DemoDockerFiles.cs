using System.Text;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.DevHost.Demo.FileSystem;

namespace Mokaterm.DevHost.Demo.Docker;

/// <summary>
/// The files the demo engine's projects and volumes point at, put into the demo session's file tree so the Docker
/// manager's files, env and volume views have something real to read: the shop project's compose file and env files
/// (one of them missing on purpose), a monitoring project whose override file is gone, and volume folders under
/// <c>/var/lib/docker</c> that only root may read. The engine reads them back from the same tree, so a file saved from
/// the manager is what its <c>test -f</c> and <c>compose config</c> see.
/// </summary>
internal static class DemoDockerFiles
{
	/// <summary>Files a search for compose files finds, beside the projects that run.</summary>
	public static readonly IReadOnlyList<string> Found =
	[
		"/srv/shop/compose.yaml",
		"/opt/legacy-crm/docker-compose.yml",
		"/home/demo/sandbox/compose.yaml",
	];

	private const string ShopCompose = """
		name: shop
		services:
		  web:
		    image: nginx:1.27-alpine
		    ports: ["8080:80"]
		    env_file:
		      - web.env
		  api:
		    image: ghcr.io/acme/shop-api:2.4.1
		    env_file: [api.env]
		    environment:
		      ConnectionStrings__Shop: Host=db;Username=shop;Password=${DB_PASSWORD}
		      SMTP_PASSWORD: ${SMTP_PASSWORD}
		    ports: ["127.0.0.1:5000:5000"]
		  db:
		    image: postgres:16
		    environment:
		      POSTGRES_PASSWORD: ${DB_PASSWORD}
		    volumes:
		      - pgdata:/var/lib/postgresql/data
		volumes:
		  pgdata:
		""";

	private const string ShopEnv = """
		# Shared by every service of the shop.
		export DB_PASSWORD=s3cr3t-demo
		COMPOSE_PROJECT_NAME=shop
		TZ="Europe/Amsterdam"
		API_TOKEN='tok_live_demo_9f8e7d' # rotated monthly
		EMPTY_ONE=
		""";

	/// <summary>What the demo builds from: <c>/srv/shop/api</c> is a build context with a Dockerfile in it.</summary>
	private const string ApiDockerfile = """
		FROM node:22-alpine AS build
		WORKDIR /app
		COPY package*.json ./
		RUN npm ci --omit=dev
		COPY . .

		FROM node:22-alpine
		WORKDIR /app
		COPY --from=build /app .
		CMD ["node", "server.js"]
		""";

	private const string WebEnv = """
		NGINX_PORT=80
		NGINX_ENTRYPOINT_QUIET_LOGS=1
		""";

	/// <summary>
	/// The engine's view of <paramref name="tree"/>: a file's text as root reads it, or null when there is no such file.
	/// The daemon and compose run as root here, so permissions never hide a file from them.
	/// </summary>
	public static Func<string, string?> Reader(DemoFileTree tree)
	{
		Func<string, byte[]?> bytes = ByteReader(tree);
		return path => bytes(path) is { } content ? Encoding.UTF8.GetString(content) : null;
	}

	/// <summary><see cref="Reader"/> as bytes, for a file the host pipes into a container as it is.</summary>
	public static Func<string, byte[]?> ByteReader(DemoFileTree tree)
	{
		ArgumentNullException.ThrowIfNull(tree);
		return path =>
		{
			try
			{
				return tree.Read(path, DemoAccount.Root).Content;
			}
			catch (RemoteFileSystemException)
			{
				return null;
			}
		};
	}

	public static void Add(DemoNode tree, DateTimeOffset now)
	{
		ArgumentNullException.ThrowIfNull(tree);
		DemoNode srv = tree.Child("srv") ?? tree.Add(DemoNode.NewDirectory("srv", "root", "root", DemoModes.Octal("755"), now.AddDays(-200)));
		DemoNode shop = srv.Add(DemoNode.NewDirectory("shop", "root", "docker", DemoModes.Octal("775"), now.AddDays(-60)));
		Text(shop, "compose.yaml", "644", now.AddDays(-3), ShopCompose);
		Text(shop, ".env", "640", now.AddDays(-20), ShopEnv);
		Text(shop, "web.env", "644", now.AddDays(-20), WebEnv);
		DemoNode api = shop.Add(DemoNode.NewDirectory("api", "root", "docker", DemoModes.Octal("775"), now.AddDays(-2)));
		Text(api, "Dockerfile", "644", now.AddDays(-2), ApiDockerfile);
		Text(api, "package.json", "644", now.AddDays(-2), "{\n  \"name\": \"shop-api\",\n  \"version\": \"2.4.2\"\n}\n");

		DemoNode opt = tree.Child("opt") ?? tree.Add(DemoNode.NewDirectory("opt", "root", "root", DemoModes.Octal("755"), now.AddDays(-200)));
		DemoNode monitoring = opt.Add(DemoNode.NewDirectory("monitoring", "root", "root", DemoModes.Octal("755"), now.AddDays(-15)));
		Text(monitoring, "docker-compose.yml", "644", now.AddDays(-15), "services:\n  grafana:\n    image: grafana/grafana:11.2.0\n  prometheus:\n    image: prom/prometheus:v2.54.1\n    restart: always\n    mem_limit: 512m\n");

		DemoNode var = tree.Child("var") ?? tree.Add(DemoNode.NewDirectory("var", "root", "root", DemoModes.Octal("755"), now.AddDays(-200)));
		DemoNode lib = var.Child("lib") ?? var.Add(DemoNode.NewDirectory("lib", "root", "root", DemoModes.Octal("755"), now.AddDays(-200)));
		DemoNode volumes = lib.Add(DemoNode.NewDirectory("docker", "root", "root", DemoModes.Octal("710"), now.AddDays(-200)))
			.Add(DemoNode.NewDirectory("volumes", "root", "root", DemoModes.Octal("701"), now.AddDays(-2)));

		DemoNode pgdata = Data(volumes, "shop_pgdata", "999", now);
		Text(pgdata, "PG_VERSION", "600", now.AddDays(-60), "16\n", "999");
		Text(pgdata, "postgresql.auto.conf", "600", now.AddDays(-60), "# Do not edit this file manually!\nlisten_addresses = '*'\n", "999");
		pgdata.Add(DemoNode.NewDirectory("base", "999", "999", DemoModes.Octal("700"), now.AddHours(-1)));

		DemoNode grafana = Data(volumes, "monitoring_grafana", "472", now);
		byte[] database = [.. "SQLite format 3\0"u8, 0x10, 0x00, 0x01, 0x01];
		grafana.Add(DemoNode.NewFile("grafana.db", "472", "472", DemoModes.Octal("640"), now.AddDays(-15), 2_097_152, database));
		Data(volumes, "old-cache", "root", now);
	}

	/// <summary><c>/var/lib/docker/volumes/NAME/_data</c>, owned the way the image's user leaves it.</summary>
	private static DemoNode Data(DemoNode volumes, string name, string owner, DateTimeOffset now) =>
		volumes.Add(DemoNode.NewDirectory(name, "root", "root", DemoModes.Octal("755"), now.AddDays(-30)))
			.Add(DemoNode.NewDirectory("_data", owner, owner, DemoModes.Octal("700"), now.AddHours(-1)));

	private static void Text(DemoNode parent, string name, string mode, DateTimeOffset modified, string text, string owner = "root")
	{
		byte[] content = Encoding.UTF8.GetBytes(text.ReplaceLineEndings("\n"));
		parent.Add(DemoNode.NewFile(name, owner, owner == "root" ? "docker" : owner, DemoModes.Octal(mode), modified, content.Length, content));
	}
}
