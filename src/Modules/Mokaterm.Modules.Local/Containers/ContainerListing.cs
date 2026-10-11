using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using Mokaterm.Modules.Local.Shells;

namespace Mokaterm.Modules.Local.Containers;

/// <summary>
/// Lists what a container or pod login could open, for the options editor's "Choose": <c>docker ps</c> and
/// <c>kubectl get pods</c> against the daemon or cluster the login's own options name.
/// </summary>
internal static partial class ContainerListing
{
	/// <summary>A listing is a glance, not a job: a daemon or cluster that takes longer is reported as not answering.</summary>
	public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

	/// <summary>
	/// What is kept of a listing. A pod's JSON runs to several kilobytes, so this holds a namespace of a thousand or
	/// more; past it the list says it was cut, and the name can still be typed.
	/// </summary>
	public const int OutputLimitBytes = 16 * 1024 * 1024;

	private const int MaxProblemLength = 400;

	public static async Task<ContainerChoiceList> DockerAsync(
		DockerShellOptions options,
		LocalMachine machine,
		ILocalCommandRunner runner,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(options);
		ArgumentNullException.ThrowIfNull(machine);
		ArgumentNullException.ThrowIfNull(runner);
		if (!DockerExecLaunch.TryListArguments(options, out IReadOnlyList<string>? arguments, out string? problem))
		{
			return ContainerChoiceList.Failed(problem);
		}

		if (ContainerCli.FindDocker(machine) is not { } docker)
		{
			return ContainerChoiceList.Failed(ContainerCli.DockerMissing);
		}

		(LocalCommandResult? result, problem) = await RunAsync(runner, docker, ContainerCli.Docker, arguments, cancellationToken);
		return result is null
			? ContainerChoiceList.Failed(problem!)
			: new ContainerChoiceList { Choices = ContainerChoices.FromDockerPs(result.Output), IsCut = result.OutputCut };
	}

	public static async Task<ContainerChoiceList> KubernetesAsync(
		KubernetesShellOptions options,
		LocalMachine machine,
		ILocalCommandRunner runner,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(options);
		ArgumentNullException.ThrowIfNull(machine);
		ArgumentNullException.ThrowIfNull(runner);
		if (!KubernetesExecLaunch.TryListArguments(options, out IReadOnlyList<string>? arguments, out string? problem))
		{
			return ContainerChoiceList.Failed(problem);
		}

		if (ContainerCli.FindKubectl(machine) is not { } kubectl)
		{
			return ContainerChoiceList.Failed(ContainerCli.KubectlMissing);
		}

		(LocalCommandResult? result, problem) = await RunAsync(runner, kubectl, ContainerCli.Kubectl, arguments, cancellationToken);
		if (result is null)
		{
			return ContainerChoiceList.Failed(problem!);
		}

		// Cut JSON does not parse, so a cut listing is reported as one rather than as an empty namespace.
		return result.OutputCut
			? ContainerChoiceList.Failed("The namespace has more pods than a list here holds. Type the pod's name instead.")
			: new ContainerChoiceList { Choices = ContainerChoices.FromKubectlPods(result.Output) };
	}

	private static async Task<(LocalCommandResult? Result, string? Problem)> RunAsync(
		ILocalCommandRunner runner,
		string program,
		string tool,
		IReadOnlyList<string> arguments,
		CancellationToken cancellationToken)
	{
		LocalCommandResult result;
		try
		{
			result = await runner.RunAsync(program, arguments, Timeout, OutputLimitBytes, cancellationToken);
		}
		catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
		{
			return (null, $"{tool} could not be started: {ex.Message}");
		}

		if (result.TimedOut)
		{
			return (null, string.Create(CultureInfo.InvariantCulture, $"{tool} did not answer within {Timeout.TotalSeconds:0} seconds."));
		}

		if (result.ExitCode is not 0)
		{
			return (null, FirstLine(result.Error) ?? string.Create(CultureInfo.InvariantCulture, $"{tool} exited with code {result.ExitCode}."));
		}

		return (result, null);
	}

	/// <summary>
	/// The tool's own first line of complaint, which names the actual trouble (a daemon that is not running, a login that
	/// expired). kubectl writes a trace line per retry before its sentence (<c>E1011 04:29:01.009226 39456 memcache.go:265]</c>),
	/// so those are passed over while anything else is there.
	/// </summary>
	internal static string? FirstLine(string? text)
	{
		string[] lines = [.. (text ?? "")
			.ReplaceLineEndings("\n")
			.Split('\n')
			.Select(entry => new string([.. entry.Where(character => !char.IsControl(character))]).Trim())
			.Where(entry => entry.Length > 0)];
		string? line = lines.FirstOrDefault(entry => !TraceLine().IsMatch(entry)) ?? lines.FirstOrDefault();
		return line is null ? null : line.Length <= MaxProblemLength ? line : string.Concat(line.AsSpan(0, MaxProblemLength), "...");
	}

	[GeneratedRegex(@"^[IWEF]\d{4} \d{2}:\d{2}:\d{2}\.\d+\s+\d+ [^\s\]]+:\d+\]", RegexOptions.CultureInvariant)]
	private static partial Regex TraceLine();
}
