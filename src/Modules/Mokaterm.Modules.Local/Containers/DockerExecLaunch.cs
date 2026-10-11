using System.Diagnostics.CodeAnalysis;
using Mokaterm.Abstractions.Containers;
using Mokaterm.Modules.Local.Pty;
using Mokaterm.Modules.Local.Shells;

namespace Mokaterm.Modules.Local.Containers;

/// <summary>
/// Builds <c>docker exec</c> for a container login: which daemon, which container, as whom, where, and what runs there.
/// Every value goes in <c>--name=value</c> form, so nothing a login holds can be read as another option.
/// </summary>
internal static class DockerExecLaunch
{
	public const string DisplayName = "docker exec";

	public const string ListFormat = "{{json .}}";

	public const string NoCommand = "Name the command this login runs in the container in its options.";

	/// <summary>The docker CLI's switch for the suggestions it prints after a command.</summary>
	public const string HintsVariable = "DOCKER_CLI_HINTS";

	public static bool TryBuild(
		string address,
		DockerShellOptions options,
		string docker,
		LocalMachine machine,
		string terminalType,
		[NotNullWhen(true)] out PseudoTerminalLaunch? launch,
		[NotNullWhen(true)] out string? container,
		[NotNullWhen(false)] out string? problem)
	{
		ArgumentNullException.ThrowIfNull(options);
		ArgumentNullException.ThrowIfNull(docker);
		ArgumentNullException.ThrowIfNull(machine);
		ArgumentNullException.ThrowIfNull(terminalType);
		launch = null;
		if (!TryTarget(address, options, out container, out problem) || !TryDaemon(options, out List<string>? arguments, out problem))
		{
			container = null;
			return false;
		}

		string? user = ContainerNames.Value(options.User);
		string? folder = ContainerNames.Value(options.Folder);
		string? command = ContainerNames.Value(options.Command);
		problem = (user is null ? null : ContainerNames.UserProblem(user))
			?? (folder is null ? null : ContainerNames.FolderProblem(folder))
			?? (options.Shell == ContainerShell.Custom && command is null ? NoCommand : null);
		if (problem is not null)
		{
			container = null;
			return false;
		}

		arguments.AddRange(["exec", "--interactive", "--tty"]);
		if (user is not null)
		{
			arguments.Add($"--user={user}");
		}

		if (folder is not null)
		{
			arguments.Add($"--workdir={folder}");
		}

		// docker exec sets TERM=xterm on its own; the name the terminal settings use is the one the view really speaks.
		arguments.Add($"--env=TERM={terminalType}");
		arguments.Add(container);
		arguments.AddRange(ContainerShellCommand.Words(options.Shell));

		// Docker Desktop's CLI prints a "What's next" advert into the terminal after a failed exec, above its own error.
		// A user who set the variable themselves keeps their choice.
		Dictionary<string, string> environment = LocalLaunchBuilder.InheritedEnvironment(machine);
		environment.TryAdd(HintsVariable, "false");
		launch = new PseudoTerminalLaunch
		{
			Program = docker,
			Arguments = arguments,
			ExtraArguments = options.Shell == ContainerShell.Custom ? command : null,
			WorkingDirectory = machine.UserProfile,
			Environment = environment,
		};
		return true;
	}

	/// <summary>The running containers of the login's daemon, one JSON object per line: what "Choose" lists.</summary>
	public static bool TryListArguments(
		DockerShellOptions options,
		[NotNullWhen(true)] out IReadOnlyList<string>? arguments,
		[NotNullWhen(false)] out string? problem)
	{
		ArgumentNullException.ThrowIfNull(options);
		arguments = null;
		if (!TryDaemon(options, out List<string>? list, out problem))
		{
			return false;
		}

		list.AddRange(["ps", $"--format={ListFormat}"]);
		arguments = list;
		return true;
	}

	/// <summary>The container the login opens: its own option first, then the host's address.</summary>
	public static bool TryTarget(
		string? address,
		DockerShellOptions options,
		[NotNullWhen(true)] out string? container,
		[NotNullWhen(false)] out string? problem)
	{
		ArgumentNullException.ThrowIfNull(options);
		container = null;
		string? named = ContainerNames.Value(options.Container) ?? ContainerNames.Value(address);
		problem = DockerNames.ContainerProblem(named);
		if (problem is not null)
		{
			return false;
		}

		// The rule refuses a missing name, so one that passed is there.
		container = named!;
		return true;
	}

	/// <summary>The global options that pick the daemon: a context, a host, or neither for docker's current one.</summary>
	private static bool TryDaemon(
		DockerShellOptions options,
		[NotNullWhen(true)] out List<string>? arguments,
		[NotNullWhen(false)] out string? problem)
	{
		arguments = null;
		string? context = ContainerNames.Value(options.Context);
		string? host = ContainerNames.Value(options.Host);
		problem = context is not null && host is not null
			? "Name a Docker context or a Docker host, not both: docker refuses the two together."
			: (context is null ? null : ContainerNames.DockerContextProblem(context))
				?? (host is null ? null : ContainerNames.DockerHostProblem(host));
		if (problem is not null)
		{
			return false;
		}

		arguments = [];
		if (context is not null)
		{
			arguments.Add($"--context={context}");
		}
		else if (host is not null)
		{
			arguments.Add($"--host={host}");
		}

		return true;
	}
}
