using System.Globalization;
using Mokaterm.Abstractions.Containers;

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
	/// Removes stopped containers, networks no container uses, dangling images and unused build cache; volumes stay,
	/// because <c>--volumes</c> is not passed.
	/// </summary>
	public static DockerCall SystemPrune() => Docker(["system", "prune", "--force"]).AsChange(DockerCall.LongTimeout);

	public static DockerCall ListContainers() => Docker(["ps", "--all", "--no-trunc", "--format", JsonLines]);

	public static DockerCall Inspect(string container) => Docker(["container", "inspect", Container(container)]);

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
			if (!DockerLogParser.IsTimestamp(since) || since.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)))
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
	/// Prints <c>bash</c> when the container has it. Run before typing a shell into the terminal, so the typed command
	/// is the plain one a person would use. Needs <c>sh</c>, which every image with a shell has.
	/// </summary>
	public static DockerCall FindShell(string container) =>
		Docker(["exec", Container(container), "sh", "-c", "command -v bash"]) with { Timeout = TimeSpan.FromSeconds(15) };

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
	/// The line typed into the session's terminal to open a shell in a container, Enter included. Typed rather than run,
	/// so the shell belongs to the terminal; a name with a control character is refused because the terminal would read
	/// it as keys.
	/// </summary>
	public static string ShellCommandLine(string container, string shell, bool elevated)
	{
		if (shell is not ("bash" or "sh"))
		{
			throw new ArgumentException("Only bash and sh are typed.", nameof(shell));
		}

		string line = DockerCommandLine.Build(Docker(["exec", "-it", Container(container), shell]));
		return (elevated ? "sudo " + line : line) + "\r";
	}

	public static DockerCall ListImages() => Docker(["image", "ls", "--no-trunc", "--format", JsonLines]);

	public static DockerCall Pull(string reference) => Docker(["pull", Image(reference)]).AsChange(DockerCall.LongTimeout);

	/// <param name="force">Removes it even when containers use it or several tags point at it (<c>--force</c>).</param>
	public static DockerCall RemoveImage(string reference, bool force) =>
		Docker(force ? ["image", "rm", "--force", Image(reference)] : ["image", "rm", Image(reference)]).AsChange();

	/// <summary>Removes images no tag points at and no container uses. Tagged images stay, because <c>--all</c> is not passed.</summary>
	public static DockerCall PruneDanglingImages() => Docker(["image", "prune", "--force"]).AsChange(DockerCall.LongTimeout);

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
