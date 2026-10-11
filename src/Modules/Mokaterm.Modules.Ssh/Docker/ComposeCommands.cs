using Mokaterm.Abstractions.Containers;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// The compose commands the manager runs. Each one runs in the project's folder with <c>-p</c>, every config file and
/// every env file it was started with, so compose finds the same project, the same <c>.env</c> and the same files it
/// was started with, whichever folder the login shell starts in. A service name narrows a command to that service.
/// </summary>
internal static class ComposeCommands
{
	/// <summary><c>ls --all --format json</c>; only a compose that <see cref="ComposeTool.SupportsList"/> has it.</summary>
	public static DockerCall List(ComposeTool tool)
	{
		ArgumentNullException.ThrowIfNull(tool);
		return new DockerCall { Program = tool.Program, Arguments = ["ls", "--all", "--format", "json"] };
	}

	public static DockerCall Containers(ComposeTarget target) => Compose(target, ["ps", "--all", "--format", "json"]);

	/// <summary>The services the files define, one per line, including those with no container yet.</summary>
	public static DockerCall Services(ComposeTarget target) => Compose(target, ["config", "--services"]);

	/// <summary>The project as compose resolves it: every file merged, variables filled in.</summary>
	public static DockerCall Config(ComposeTarget target) => Compose(target, ["config"]) with { OutputLimitBytes = 2 * 1024 * 1024 };

	/// <summary>The same as JSON, which 2.x prints and early 2.x releases refuse; it names every service's env files.</summary>
	public static DockerCall ConfigJson(ComposeTarget target) =>
		Compose(target, ["config", "--format", "json"]) with { OutputLimitBytes = 2 * 1024 * 1024 };

	/// <summary>Checks the files and prints nothing but its warnings, such as a variable the files use and nothing sets.</summary>
	public static DockerCall ConfigCheck(ComposeTarget target) => Compose(target, ["config", "--quiet"]);

	/// <param name="build">Builds images first (<c>--build</c>).</param>
	/// <param name="forceRecreate">Recreates containers whose configuration did not change too (<c>--force-recreate</c>).</param>
	/// <param name="service">One service only, left alone by the ones it depends on (<c>--no-deps</c>), or null for all.</param>
	public static DockerCall Up(ComposeTarget target, bool build = false, bool forceRecreate = false, string? service = null)
	{
		List<string> words = ["up", "--detach"];
		if (build)
		{
			words.Add("--build");
		}

		if (forceRecreate)
		{
			words.Add("--force-recreate");
		}

		if (service is not null)
		{
			words.Add("--no-deps");
			words.Add(Service(service));
		}

		return Compose(target, words).AsChange(DockerCall.LongTimeout);
	}

	/// <summary><c>up --detach --force-recreate --no-deps SERVICE</c>: that service's containers made again, nothing else touched.</summary>
	public static DockerCall Recreate(ComposeTarget target, string service) => Up(target, forceRecreate: true, service: service);

	/// <param name="removeVolumes">Also removes the named volumes the file declares and the anonymous ones (<c>--volumes</c>).</param>
	public static DockerCall Down(ComposeTarget target, bool removeVolumes) =>
		Compose(target, removeVolumes ? ["down", "--volumes"] : ["down"]).AsChange();

	public static DockerCall Restart(ComposeTarget target, string? service = null) => Compose(target, WithService("restart", service)).AsChange();

	public static DockerCall Stop(ComposeTarget target, string? service = null) => Compose(target, WithService("stop", service)).AsChange();

	public static DockerCall Start(ComposeTarget target, string? service = null) => Compose(target, WithService("start", service)).AsChange();

	public static DockerCall Pull(ComposeTarget target, string? service = null) =>
		Compose(target, WithService("pull", service)).AsChange(DockerCall.LongTimeout);

	/// <param name="service">One service, or null for all of them.</param>
	/// <param name="since">The newest timestamp already shown, for following; the Python compose has no <c>--since</c>.</param>
	public static DockerCall Logs(ComposeTarget target, string? service, int tail, string? since = null)
	{
		ArgumentNullException.ThrowIfNull(target);
		List<string> words = ["logs", "--no-color", "--timestamps", "--tail", DockerCommands.Tail(tail)];
		if (since is not null)
		{
			if (target.Tool.IsLegacy)
			{
				throw new ArgumentException("docker-compose 1.x has no --since.", nameof(since));
			}

			if (!DockerLogParser.IsTimestamp(since) || since.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)))
			{
				throw new ArgumentException("That is not a docker timestamp.", nameof(since));
			}

			words.Add("--since");
			words.Add(since);
		}

		if (service is not null)
		{
			words.Add(Service(service));
		}

		return Compose(target, words) with { OutputLimitBytes = 4 * 1024 * 1024 };
	}

	/// <summary>
	/// <c>cd -- DIR &amp;&amp; docker compose -p NAME -f FILE... --env-file FILE... WORDS</c>. A project whose files sit
	/// somewhere other than its folder (it was started with <c>--project-directory</c>) is told its folder again, since
	/// compose would otherwise take the first file's.
	/// </summary>
	private static DockerCall Compose(ComposeTarget target, IReadOnlyList<string> words)
	{
		ArgumentNullException.ThrowIfNull(target);
		List<string> arguments = [];
		if (target.Project is { } project)
		{
			arguments.Add("-p");
			arguments.Add(DockerCommands.Checked(project, DockerNames.ProjectProblem(project)));
		}

		string? directory = target.WorkingDirectory;
		if (directory is not null)
		{
			_ = DockerCommands.Checked(directory, DockerNames.PathProblem(directory, "project folder"));
		}

		foreach (string file in target.ConfigFiles)
		{
			arguments.Add("-f");
			arguments.Add(DockerCommands.Checked(file, DockerNames.PathProblem(file, "compose file")));
		}

		if (directory is not null && target.ConfigFiles.Count > 0 && !string.Equals(DockerHostPaths.DirectoryOf(target.ConfigFiles[0]), directory, StringComparison.Ordinal))
		{
			arguments.Add("--project-directory");
			arguments.Add(directory);
		}

		foreach (string file in target.EnvironmentFiles)
		{
			arguments.Add("--env-file");
			arguments.Add(DockerCommands.Checked(file, DockerNames.PathProblem(file, "env file")));
		}

		arguments.AddRange(words);
		return new DockerCall { Program = target.Tool.Program, Arguments = arguments, WorkingDirectory = directory };
	}

	private static string[] WithService(string command, string? service) => service is null ? [command] : [command, Service(service)];

	private static string Service(string service) => DockerCommands.Checked(service, DockerNames.ServiceProblem(service));
}
