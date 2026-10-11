using System.Diagnostics.CodeAnalysis;
using Mokaterm.Modules.Local.Pty;
using Mokaterm.Modules.Local.Shells;

namespace Mokaterm.Modules.Local.Containers;

/// <summary>
/// Builds <c>kubectl exec</c> for a pod login: which cluster, which namespace, which pod and container, and what runs
/// there. Every value goes in <c>--name=value</c> form, and the command follows <c>--</c>, so nothing a login holds can be
/// read as another option.
/// </summary>
internal static class KubernetesExecLaunch
{
	public const string DisplayName = "kubectl exec";

	public static bool TryBuild(
		string address,
		KubernetesShellOptions options,
		string kubectl,
		LocalMachine machine,
		[NotNullWhen(true)] out PseudoTerminalLaunch? launch,
		[NotNullWhen(true)] out string? pod,
		[NotNullWhen(false)] out string? problem)
	{
		ArgumentNullException.ThrowIfNull(options);
		ArgumentNullException.ThrowIfNull(kubectl);
		ArgumentNullException.ThrowIfNull(machine);
		launch = null;
		if (!TryTarget(address, options, out pod, out problem) || !TryCluster(options, out List<string>? arguments, out problem))
		{
			pod = null;
			return false;
		}

		string? container = ContainerNames.Value(options.Container);
		string? command = ContainerNames.Value(options.Command);
		problem = (container is null ? null : ContainerNames.PodContainerProblem(container))
			?? (options.Shell == ContainerShell.Custom && command is null ? DockerExecLaunch.NoCommand : null);
		if (problem is not null)
		{
			pod = null;
			return false;
		}

		arguments.AddRange(["exec", "--stdin", "--tty", pod]);
		if (container is not null)
		{
			arguments.Add($"--container={container}");
		}

		arguments.Add("--");
		arguments.AddRange(ContainerShellCommand.Words(options.Shell));
		launch = new PseudoTerminalLaunch
		{
			Program = kubectl,
			Arguments = arguments,
			ExtraArguments = options.Shell == ContainerShell.Custom ? command : null,
			WorkingDirectory = machine.UserProfile,
			Environment = LocalLaunchBuilder.InheritedEnvironment(machine),
		};
		return true;
	}

	/// <summary>The pods of the login's namespace as one JSON document: what "Choose" lists.</summary>
	public static bool TryListArguments(
		KubernetesShellOptions options,
		[NotNullWhen(true)] out IReadOnlyList<string>? arguments,
		[NotNullWhen(false)] out string? problem)
	{
		ArgumentNullException.ThrowIfNull(options);
		arguments = null;
		if (!TryCluster(options, out List<string>? list, out problem))
		{
			return false;
		}

		list.AddRange(["get", "pods", "--output=json"]);
		arguments = list;
		return true;
	}

	/// <summary>The pod the login opens: its own option first, then the host's address.</summary>
	public static bool TryTarget(
		string? address,
		KubernetesShellOptions options,
		[NotNullWhen(true)] out string? pod,
		[NotNullWhen(false)] out string? problem)
	{
		ArgumentNullException.ThrowIfNull(options);
		pod = null;
		string? named = ContainerNames.Value(options.Pod) ?? ContainerNames.Value(address);
		problem = ContainerNames.PodProblem(named);
		if (problem is not null)
		{
			return false;
		}

		// The rule refuses a missing name, so one that passed is there.
		pod = named!;
		return true;
	}

	/// <summary>The global options that pick the cluster and the namespace, or neither for kubectl's current ones.</summary>
	private static bool TryCluster(
		KubernetesShellOptions options,
		[NotNullWhen(true)] out List<string>? arguments,
		[NotNullWhen(false)] out string? problem)
	{
		arguments = null;
		string? context = ContainerNames.Value(options.Context);
		string? space = ContainerNames.Value(options.Namespace);
		problem = (context is null ? null : ContainerNames.KubeContextProblem(context))
			?? (space is null ? null : ContainerNames.NamespaceProblem(space));
		if (problem is not null)
		{
			return false;
		}

		arguments = [];
		if (context is not null)
		{
			arguments.Add($"--context={context}");
		}

		if (space is not null)
		{
			arguments.Add($"--namespace={space}");
		}

		return true;
	}
}
