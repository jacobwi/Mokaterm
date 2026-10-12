using System.Globalization;
using System.Text;

namespace Mokaterm.DevHost.Demo.Docker;

/// <summary>The <c>docker compose</c> commands the manager sends: projects come and go with their containers, as they do.</summary>
internal sealed partial class DemoDockerEngine
{
	private const string ComposeVersion = "2.29.7";

	private const string ShopConfig = """
		name: shop
		services:
		  api:
		    depends_on:
		      db:
		        condition: service_healthy
		    environment:
		      ASPNETCORE_URLS: http://+:5000
		      ConnectionStrings__Shop: Host=db;Username=shop;Password=s3cr3t-demo
		    image: ghcr.io/acme/shop-api:2.4.1
		    networks:
		      default: null
		    ports:
		      - mode: ingress
		        host_ip: 127.0.0.1
		        target: 5000
		        published: "5000"
		        protocol: tcp
		  db:
		    environment:
		      POSTGRES_DB: shop
		      POSTGRES_PASSWORD: s3cr3t-demo
		      POSTGRES_USER: shop
		    healthcheck:
		      test:
		        - CMD-SHELL
		        - pg_isready -U shop
		      interval: 10s
		    image: postgres:16
		    networks:
		      default: null
		    volumes:
		      - type: volume
		        source: pgdata
		        target: /var/lib/postgresql/data
		  web:
		    image: nginx:1.27-alpine
		    networks:
		      default: null
		    ports:
		      - mode: ingress
		        target: 80
		        published: "8080"
		        protocol: tcp
		networks:
		  default:
		    name: shop_default
		volumes:
		  pgdata:
		    name: shop_pgdata
		""";

	/// <summary>What <c>config --format json</c> prints for the shop, cut to what the manager reads: each service's env files.</summary>
	private const string ShopConfigJson = """
		{"name":"shop","services":{"web":{"image":"nginx:1.27-alpine","env_file":["/srv/shop/web.env"]},"api":{"image":"ghcr.io/acme/shop-api:2.4.1","env_file":[{"path":"/srv/shop/api.env","required":true}]},"db":{"image":"postgres:16"}}}
		""";

	private DemoDockerResult Compose(string[] words, string? directory)
	{
		if (words is ["version", ..])
		{
			return DemoDockerResult.Ok(ComposeVersion + "\n");
		}

		if (words is ["ls", ..])
		{
			return DemoDockerResult.Ok(Line(Projects().Select(project => new
			{
				Name = project.Key,
				Status = string.Join(", ", project.Value.GroupBy(container => container.State).Select(group => $"{group.Key}({group.Count()})")),
				ConfigFiles = project.Value[0].Labels["com.docker.compose.project.config_files"],
			})) + "\n");
		}

		// -p NAME, then -f FILE pairs and --project-directory, then the command itself.
		string? project = null;
		int index = 0;
		while (index < words.Length && words[index].StartsWith('-'))
		{
			if (words[index] == "-p")
			{
				project = words[index + 1];
			}

			index += 2;
		}

		string[] command = words[index..];
		if (project is null)
		{
			// A file found on disk, brought up without -p: the demo says what compose would and keeps no project of it.
			string name = directory is null ? "project" : directory[(directory.LastIndexOf('/') + 1)..];
			return command is ["up", ..]
				? new DemoDockerResult(0, "", $" Network {name}_default  Created\n Container {name}-app-1  Started\n")
				: DemoDockerResult.Fail(1, "no configuration file provided: not found");
		}

		bool hasFiles = project == "shop" ? directory == ShopFolder : directory == MonitoringFolder;
		if (project == "shop" && hasFiles && command is ["config", ..] && ShopProblem() is { } problem)
		{
			return DemoDockerResult.Fail(15, problem);
		}

		List<DemoDockerContainer> members = [.. _containers.Where(container => container.Project == project)];
		string? service = command.Length > 1 && !command[^1].StartsWith('-') ? command[^1] : null;
		List<DemoDockerContainer> chosen = [.. members.Where(container => service is null || container.Service == service)];
		return command switch
		{
			["ps", ..] => DemoDockerResult.Ok(string.Concat(members.Select(container => Line(ComposeRow(container)) + "\n"))),
			["config", "--services"] => hasFiles
				? DemoDockerResult.Ok(project == "shop" ? "web\napi\ndb\n" : "grafana\nprometheus\n")
				: DemoDockerResult.Fail(1, "no configuration file provided: not found"),
			["config", "--format", "json"] => project == "shop" && hasFiles
				? DemoDockerResult.Ok(ShopConfigJson + "\n")
				: DemoDockerResult.Fail(15, $"open {directory}/docker-compose.override.yml: no such file or directory"),
			["config", "--quiet"] => project == "shop"
				? new DemoDockerResult(0, "", "time=\"2026-10-11T07:00:00Z\" level=warning msg=\"The \\\"SMTP_PASSWORD\\\" variable is not set. Defaulting to a blank string.\"\n")
				: DemoDockerResult.Fail(15, $"open {directory}/docker-compose.override.yml: no such file or directory"),
			["config"] => project == "shop" && hasFiles
				? DemoDockerResult.Ok(ShopConfig + "\n")
				: DemoDockerResult.Fail(15, $"open {directory}/docker-compose.override.yml: no such file or directory"),
			["up", ..] when command.Contains("--force-recreate") => Each(chosen, "Recreate", "Started", container => Start(container)),
			["up", ..] => Up(project, members, command.Contains("--build")),
			["down", ..] => Down(project, members, command.Contains("--volumes")),
			["stop", ..] => Each(chosen, "Stopping", "Stopped", container => Stop(container, 0)),
			["start", ..] => Each(chosen, "Starting", "Started", container => Start(container)),
			["restart", ..] => Each(chosen, "Restarting", "Started", container => Start(container)),
			["pull", ..] => ComposePull(chosen),
			["logs", .. var rest] => ComposeLogs(members, rest),
			_ => DemoDockerResult.Fail(1, $"unknown docker command: \"compose {string.Join(' ', command)}\""),
		};
	}

	/// <summary>
	/// What compose would say about the shop's files as the tree holds them now, or null when they read: a file saved
	/// from the manager that breaks them fails <c>config</c> the way a real one would.
	/// </summary>
	private string? ShopProblem()
	{
		if (_readFile(ShopFile) is not { } compose)
		{
			return $"open {ShopFile}: no such file or directory";
		}

		return DemoComposeCheck.Yaml(compose)
			?? (_readFile($"{ShopFolder}/.env") is { } env ? DemoComposeCheck.Env($"{ShopFolder}/.env", env) : null);
	}

	private Dictionary<string, List<DemoDockerContainer>> Projects()
	{
		Dictionary<string, List<DemoDockerContainer>> projects = new(StringComparer.Ordinal);
		foreach (DemoDockerContainer container in _containers)
		{
			if (container.Project is { } project)
			{
				if (!projects.TryGetValue(project, out List<DemoDockerContainer>? list))
				{
					list = [];
					projects[project] = list;
				}

				list.Add(container);
			}
		}

		return projects;
	}

	private object ComposeRow(DemoDockerContainer container) => new
	{
		Command = $"\"{container.Command}\"",
		CreatedAt = ListingTime(container.Created),
		ExitCode = container.ExitCode,
		Health = container.Health,
		ID = container.Id[..12],
		Image = container.Image,
		Name = container.Name,
		Project = container.Project,
		Publishers = container.Ports.Where(port => port.Contains("->", StringComparison.Ordinal)).Select(port =>
		{
			string[] parts = port.Split("->");
			int colon = parts[0].LastIndexOf(':');
			string[] inside = parts[1].Split('/');
			return new
			{
				URL = parts[0][..colon],
				TargetPort = int.Parse(inside[0], CultureInfo.InvariantCulture),
				PublishedPort = int.Parse(parts[0][(colon + 1)..], CultureInfo.InvariantCulture),
				Protocol = inside[1],
			};
		}),
		Service = container.Service,
		State = container.State,
		Status = StatusOf(container),
	};

	private DemoDockerResult Up(string project, List<DemoDockerContainer> members, bool build)
	{
		StringBuilder error = new();
		if (members.Count == 0 && project == "shop")
		{
			AddShop(_time.GetUtcNow());
			members = [.. _containers.Where(container => container.Project == project)];
			foreach (DemoDockerContainer container in members)
			{
				container.StartedAt = _time.GetUtcNow();
			}

			error.Append(" Network shop_default  Created\n");
		}

		if (build)
		{
			error.Append(" api  Built\n");
		}

		foreach (DemoDockerContainer container in members)
		{
			bool wasRunning = container.State == "running";
			if (!wasRunning)
			{
				Start(container);
			}

			error.Append(CultureInfo.InvariantCulture, $" Container {container.Name}  {(wasRunning ? "Running" : "Started")}\n");
		}

		return new DemoDockerResult(0, "", error.ToString());
	}

	private DemoDockerResult Down(string project, List<DemoDockerContainer> members, bool volumes)
	{
		StringBuilder error = new();
		foreach (DemoDockerContainer container in members)
		{
			_containers.Remove(container);
			error.Append(CultureInfo.InvariantCulture, $" Container {container.Name}  Removed\n");
		}

		if (_networks.RemoveAll(network => network.Name == project + "_default") > 0)
		{
			error.Append(CultureInfo.InvariantCulture, $" Network {project}_default  Removed\n");
		}

		if (volumes)
		{
			foreach (string volume in members.SelectMany(container => container.Volumes).Distinct().ToList())
			{
				_volumes.Remove(volume);
				error.Append(CultureInfo.InvariantCulture, $" Volume {volume}  Removed\n");
			}
		}

		return new DemoDockerResult(0, "", error.ToString());
	}

	private static DemoDockerResult Each(List<DemoDockerContainer> members, string doing, string done, Action<DemoDockerContainer> change)
	{
		StringBuilder error = new();
		foreach (DemoDockerContainer container in members)
		{
			error.Append(CultureInfo.InvariantCulture, $" Container {container.Name}  {doing}\n");
			change(container);
			error.Append(CultureInfo.InvariantCulture, $" Container {container.Name}  {done}\n");
		}

		return new DemoDockerResult(0, "", error.ToString());
	}

	private DemoDockerResult ComposeLogs(List<DemoDockerContainer> members, string[] words)
	{
		int tail = 100;
		string? service = null;
		string? since = null;
		for (int index = 0; index < words.Length; index++)
		{
			if (words[index] == "--tail")
			{
				tail = int.Parse(words[++index], CultureInfo.InvariantCulture);
			}
			else if (words[index] == "--since")
			{
				since = words[++index];
			}
			else if (!words[index].StartsWith('-'))
			{
				service = words[index];
			}
		}

		StringBuilder output = new();
		foreach (DemoDockerContainer container in members.Where(container => service is null || container.Service == service))
		{
			DemoDockerResult logs = Logs(since is null
				? ["--tail", Math.Min(tail, 40).ToString(CultureInfo.InvariantCulture), container.Name]
				: ["--tail", "10000", "--since", since, container.Name]);
			foreach (string line in (logs.Output + logs.Error).Split('\n', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal))
			{
				output.Append(container.Name.PadRight(22)).Append("| ").Append(line).Append('\n');
			}
		}

		return DemoDockerResult.Ok(output.ToString());
	}
}
