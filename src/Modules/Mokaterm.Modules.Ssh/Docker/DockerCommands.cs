using System.Globalization;
using Mokaterm.Abstractions.Containers;
using Mokaterm.Modules.Ssh.Tools;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// Every docker command the manager runs, as words. A name is checked against Docker's own rule before it becomes a
/// word (<see cref="DockerNames"/>), so nothing that reads as an option or breaks the daemon's grammar gets this far.
/// Listings print JSON through <c>{{json .}}</c>, which every version since 1.13 understands, rather than the newer
/// <c>json</c> shorthand.
/// </summary>
internal static class DockerCommands
{
	/// <summary>The template for one JSON object per line.</summary>
	public const string JsonLines = "{{json .}}";

	/// <summary>The most lines a log tail asks for.</summary>
	public const int MaxTail = 10_000;

	public static DockerCall Version() => Docker(["version", "--format", JsonLines]);

	public static DockerCall Info() => Docker(["info", "--format", JsonLines]);

	public static DockerCall ComposePluginVersion() => new()
	{
		Program = DockerProgram.ComposePlugin,
		Arguments = ["version", "--short"],
	};

	public static DockerCall ComposeStandaloneVersion() => new()
	{
		Program = DockerProgram.ComposeStandalone,
		Arguments = ["version", "--short"],
	};

	/// <summary><c>docker-compose -v</c>, which every 1.x answers, for one too old for <c>version --short</c>.</summary>
	public static DockerCall ComposeStandaloneVersionLong() => new()
	{
		Program = DockerProgram.ComposeStandalone,
		Arguments = ["-v"],
	};

	public static DockerCall DiskUsage() => Docker(["system", "df", "--format", JsonLines]);

	/// <summary>
	/// Every image, container, volume and build cache entry with its size, as one JSON object of four arrays. Sizing
	/// every volume can take the daemon a while on a big host.
	/// </summary>
	public static DockerCall SystemUsage() =>
		Docker(["system", "df", "--verbose", "--format", JsonLines]) with { Timeout = TimeSpan.FromMinutes(2) };

	/// <summary>
	/// Every build cache entry nothing uses, not just the dangling ones (<c>--all</c>): what the cleanup lists under build
	/// cache. Build cache cannot be removed by name, so this is the one cleanup that is a prune.
	/// </summary>
	public static DockerCall PruneBuildCache() => Docker(["builder", "prune", "--all", "--force"]).AsChange(DockerCall.LongTimeout);

	/// <summary>
	/// One line per container: id, restart count, state, exit code, health (<c>-</c> without a check), name and compose
	/// project. One exec channel for the whole host, through the same shell as every docker command.
	/// </summary>
	public static DockerCall ContainerStates() => DockerCall.Script(ContainerStatesScript, [ContainerStatesFormat]);

	public static DockerCall ListContainers() => Docker(["ps", "--all", "--no-trunc", "--format", JsonLines]);

	public static DockerCall Inspect(string container) => Docker(["container", "inspect", Container(container)]);

	/// <summary>Removes exactly the stopped containers named, which is what the confirmation lists. A running one is refused, not killed.</summary>
	public static DockerCall RemoveContainers(IReadOnlyList<string> containers)
	{
		ArgumentNullException.ThrowIfNull(containers);
		ArgumentOutOfRangeException.ThrowIfZero(containers.Count);
		return Docker(["rm", .. containers.Select(Container)]).AsChange();
	}

	/// <summary>Several containers in one inspect, for the mounts of a volume's users.</summary>
	public static DockerCall InspectAll(IReadOnlyList<string> containers)
	{
		ArgumentNullException.ThrowIfNull(containers);
		ArgumentOutOfRangeException.ThrowIfZero(containers.Count);
		return Docker(["container", "inspect", .. containers.Select(Container)]);
	}

	public static DockerCall InspectVolume(string name) => Docker(["volume", "inspect", Existing(name, "volume")]);

	/// <summary>Every container, running or not, that mounts the volume.</summary>
	public static DockerCall ContainersUsingVolume(string name) =>
		Docker(["ps", "--all", "--no-trunc", "--filter", "volume=" + Existing(name, "volume"), "--format", JsonLines]);

	/// <summary>One sample of CPU, memory and IO for <paramref name="containers"/>, or for every running one when none are named.</summary>
	public static DockerCall Stats(IReadOnlyList<string> containers)
	{
		ArgumentNullException.ThrowIfNull(containers);
		return Docker(["stats", "--no-stream", "--no-trunc", "--format", JsonLines, .. containers.Select(Container)]);
	}

	/// <param name="tail">How many lines from the end, 1 to <see cref="MaxTail"/>.</param>
	/// <param name="since">The newest timestamp already shown, for following; null for a fresh tail.</param>
	public static DockerCall Logs(string container, int tail, string? since)
	{
		List<string> words = ["logs", "--timestamps", "--tail", Tail(tail)];
		if (since is not null)
		{
			if (!LogLines.IsTimestampWord(since))
			{
				throw new ArgumentException("That is not a docker timestamp.", nameof(since));
			}

			words.Add("--since");
			words.Add(since);
		}

		words.Add(Container(container));
		return Docker(words) with { OutputLimitBytes = 4 * 1024 * 1024 };
	}

	/// <summary>
	/// Prints <c>bash</c> when the container has it. Run before a container's shell tab opens, so the command it types
	/// is the plain one a person would use. Needs <c>sh</c>, which every image with a shell has.
	/// </summary>
	public static DockerCall FindShell(string container) =>
		Docker(["exec", Container(container), "sh", "-c", "command -v bash"]) with { Timeout = TimeSpan.FromSeconds(15) };

	/// <summary>
	/// One of the module's file scripts run inside a running container: <c>docker exec NAME sh -c SCRIPT sh ARG...</c>, so
	/// paths stay positional arguments in the container too. stdout is kept as bytes, since a listing is NUL-separated and
	/// a read is the file itself.
	/// </summary>
	public static DockerCall ExecScript(string container, string script, IReadOnlyList<string> arguments)
	{
		ArgumentNullException.ThrowIfNull(script);
		ArgumentNullException.ThrowIfNull(arguments);
		return Docker(["exec", Container(container), "sh", "-c", script, "sh", .. arguments]) with { BinaryOutput = true };
	}

	/// <summary>
	/// <see cref="ExecScript"/> reading <paramref name="input"/>, a file on the docker host, on stdin: the host runs
	/// <c>docker exec -i NAME sh -c SCRIPT sh ARG... &lt; INPUT</c> through a fixed script of its own, so the input's path is
	/// an argument there as well.
	/// </summary>
	public static DockerCall ExecScriptWithInput(string container, string input, string script, IReadOnlyList<string> arguments)
	{
		ArgumentNullException.ThrowIfNull(script);
		ArgumentNullException.ThrowIfNull(arguments);
		string file = Checked(input, DockerNames.PathProblem(input, "input file"));
		return DockerCall.Script(DockerHostScripts.WithInputScript, [file, "docker", "exec", "-i", Container(container), "sh", "-c", script, "sh", .. arguments])
			with { BinaryOutput = true };
	}

	public static DockerCall Start(string container) => Docker(["start", Container(container)]).AsChange();

	public static DockerCall Stop(string container) => Docker(["stop", Container(container)]).AsChange();

	public static DockerCall Restart(string container) => Docker(["restart", Container(container)]).AsChange();

	public static DockerCall Pause(string container) => Docker(["pause", Container(container)]).AsChange();

	public static DockerCall Unpause(string container) => Docker(["unpause", Container(container)]).AsChange();

	public static DockerCall Kill(string container) => Docker(["kill", Container(container)]).AsChange();

	/// <param name="removeVolumes">Also removes the anonymous volumes it created (<c>--volumes</c>); named volumes always stay.</param>
	/// <param name="force">Kills it first when it is running (<c>--force</c>).</param>
	public static DockerCall Remove(string container, bool removeVolumes, bool force)
	{
		List<string> words = ["rm"];
		if (force)
		{
			words.Add("--force");
		}

		if (removeVolumes)
		{
			words.Add("--volumes");
		}

		words.Add(Container(container));
		return Docker(words).AsChange();
	}

	/// <summary>
	/// What a container's shell tab types into its login shell: <c>exec docker exec -it NAME SHELL</c>, through sudo when
	/// the manager runs as root, so sudo asks for its password in that terminal. exec puts docker in the login shell's
	/// place, so leaving the container ends the tab's session instead of dropping back to the server's prompt. A name
	/// with a control character is refused, because the terminal would read it as keys.
	/// </summary>
	public static string ShellStartupCommand(string container, string shell, bool elevated)
	{
		if (shell is not ("bash" or "sh"))
		{
			throw new ArgumentException("Only bash and sh are typed.", nameof(shell));
		}

		string line = DockerCommandLine.Build(Docker(["exec", "-it", Container(container), shell]));
		return (elevated ? "exec sudo " : "exec ") + line;
	}

	public static DockerCall ListImages() => Docker(["image", "ls", "--no-trunc", "--format", JsonLines]);

	public static DockerCall Pull(string reference) => Docker(["pull", Image(reference)]).AsChange(DockerCall.LongTimeout);

	/// <summary>
	/// <c>docker image tag SOURCE TARGET</c>: one more name for an image, which moves <paramref name="target"/> when it
	/// already names another one.
	/// </summary>
	/// <param name="source">A tag or an image id.</param>
	/// <param name="target">A new tag, checked by <see cref="DockerImageName.TagProblem"/>.</param>
	public static DockerCall Tag(string source, string target) =>
		Docker(["image", "tag", Image(source), Checked(target.Trim(), DockerImageName.TagProblem(target))]).AsChange();

	/// <summary>
	/// <c>docker push NAME:TAG</c>, or <c>docker push --all-tags REPOSITORY</c> for every local tag of a repository, under
	/// the server's own time limit and shown live.
	/// </summary>
	/// <exception cref="ArgumentException">The reference is not a tag, or the repository names a tag or digest.</exception>
	public static DockerCall Push(string reference, bool allTags)
	{
		ArgumentNullException.ThrowIfNull(reference);
		string text = reference.Trim();
		DockerCall call;
		if (allTags)
		{
			// A repository alone reads as its latest tag, so a reference with a tag or digest of its own is refused here.
			bool bare = DockerImageName.TagProblem(text) is null && !text[(text.LastIndexOf('/') + 1)..].Contains(':', StringComparison.Ordinal);
			call = Docker(["push", "--all-tags", Checked(text, bare ? null : "--all-tags takes a repository without a tag, such as registry.example.com/team/app.")]);
		}
		else
		{
			call = Docker(["push", Checked(text, DockerImageName.TagProblem(text))]);
		}

		return DockerTimeLimit.Wrap(call with { OutputLimitBytes = DockerCall.LiveOutputBytes }, DockerCall.PushTimeout);
	}

	/// <param name="force">Removes it even when containers use it or several tags point at it (<c>--force</c>).</param>
	public static DockerCall RemoveImage(string reference, bool force) =>
		Docker(force ? ["image", "rm", "--force", Image(reference)] : ["image", "rm", Image(reference)]).AsChange();

	/// <summary>Removes exactly the tags and image ids named, which is what the confirmation lists.</summary>
	public static DockerCall RemoveImages(IReadOnlyList<string> references)
	{
		ArgumentNullException.ThrowIfNull(references);
		ArgumentOutOfRangeException.ThrowIfZero(references.Count);
		return Docker(["image", "rm", .. references.Select(Image)]).AsChange(DockerCall.LongTimeout);
	}

	/// <summary>Several images in one inspect, for the digests the registries gave them and their platform.</summary>
	public static DockerCall InspectImages(IReadOnlyList<string> references)
	{
		ArgumentNullException.ThrowIfNull(references);
		ArgumentOutOfRangeException.ThrowIfZero(references.Count);
		return Docker(["image", "inspect", .. references.Select(Image)]);
	}

	/// <summary>Answers with its version where buildx is installed, which is what says <see cref="ImagetoolsInspect"/> can run.</summary>
	public static DockerCall BuildxVersion() => Docker(["buildx", "version"]) with { Timeout = TimeSpan.FromSeconds(15) };

	/// <summary>What the registry holds for the tag now, its digest first. Nothing is pulled.</summary>
	public static DockerCall ImagetoolsInspect(string reference) =>
		Docker(["buildx", "imagetools", "inspect", Image(reference)]) with { OutputLimitBytes = 256 * 1024 };

	/// <summary>The same through <c>docker manifest</c>, for a host without buildx. It prints every platform of an index.</summary>
	public static DockerCall ManifestInspect(string reference) =>
		Docker(["manifest", "inspect", "--verbose", Image(reference)]) with { OutputLimitBytes = 4 * 1024 * 1024 };

	public static DockerCall ListVolumes() => Docker(["volume", "ls", "--format", JsonLines]);

	/// <summary>The names of the volumes no container refers to, one per line.</summary>
	public static DockerCall UnusedVolumes() => Docker(["volume", "ls", "--quiet", "--filter", "dangling=true"]);

	public static DockerCall CreateVolume(string name) => Docker(["volume", "create", NewName(name, "volume")]).AsChange();

	/// <summary>Removes exactly the volumes named, which is what the confirmation lists.</summary>
	public static DockerCall RemoveVolumes(IReadOnlyList<string> names)
	{
		ArgumentNullException.ThrowIfNull(names);
		ArgumentOutOfRangeException.ThrowIfZero(names.Count);
		return Docker(["volume", "rm", .. names.Select(name => Existing(name, "volume"))]).AsChange();
	}

	public static DockerCall ListNetworks() => Docker(["network", "ls", "--no-trunc", "--format", JsonLines]);

	public static DockerCall CreateNetwork(string name) => Docker(["network", "create", NewName(name, "network")]).AsChange();

	/// <summary>Removes exactly the networks named, which is what the confirmation lists.</summary>
	public static DockerCall RemoveNetworks(IReadOnlyList<string> names)
	{
		ArgumentNullException.ThrowIfNull(names);
		ArgumentOutOfRangeException.ThrowIfZero(names.Count);
		return Docker(["network", "rm", .. names.Select(name => Existing(name, "network"))]).AsChange();
	}

	/// <summary>The template for <see cref="ContainerStates"/>, passed as an argument so its quotes never meet the script's.</summary>
	internal const string ContainerStatesFormat =
		"{{.Id}} {{.RestartCount}} {{.State.Status}} {{.State.ExitCode}} {{if .State.Health}}{{.State.Health.Status}}{{else}}-{{end}} {{.Name}} {{with .Config.Labels}}{{index . \"com.docker.compose.project\"}}{{end}}";

	/// <summary>
	/// Lists every container and inspects them all with the template in <c>$1</c>. A container removed between the two
	/// makes inspect exit 1 after printing the rest, which the reader takes as it is.
	/// </summary>
	private const string ContainerStatesScript =
		"ids=$(docker ps --all --quiet --no-trunc) || exit $?; [ -n \"$ids\" ] || exit 0; exec docker container inspect --format \"$1\" $ids";

	internal static string Tail(int lines)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(lines, 1);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(lines, MaxTail);
		return lines.ToString(CultureInfo.InvariantCulture);
	}

	private static DockerCall Docker(IReadOnlyList<string> words) => new() { Program = DockerProgram.Docker, Arguments = words };

	private static string Container(string reference) => Checked(reference, DockerNames.ContainerProblem(reference));

	private static string Image(string reference) => Checked(reference, DockerNames.ImageProblem(reference));

	private static string NewName(string name, string kind) => Checked(name, DockerNames.NewNameProblem(name, kind));

	private static string Existing(string name, string kind) => Checked(name, DockerNames.ExistingNameProblem(name, kind));

	/// <exception cref="ArgumentException">The name breaks Docker's rule; the message says how.</exception>
	internal static string Checked(string value, string? problem) =>
		problem is null ? value : throw new ArgumentException(problem, nameof(value));
}
