using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Modules.Local.Pty;
using Mokaterm.Modules.Local.Sessions;
using Mokaterm.Modules.Local.Shells;

namespace Mokaterm.Modules.Local.Containers;

/// <summary>
/// Opens a shell in a Kubernetes pod through this computer's kubectl.exe, in a pseudo console like a local shell.
/// kubectl reads the kubeconfig and runs its own credential plugins, so a cluster that needs a cloud login works the
/// way it does in a terminal.
/// </summary>
internal sealed class KubernetesShellProvider : IProtocolProvider
{
	public const string ProtocolId = "kubernetes";

	private readonly ISettingsService _settings;
	private readonly LocalSessionStarter _starter;

	public KubernetesShellProvider(ISettingsService settings, ILoggerFactory loggerFactory)
	{
		_settings = settings;
		_starter = new LocalSessionStarter(loggerFactory);
	}

	public ProtocolDescriptor Descriptor { get; } = new()
	{
		Id = ProtocolId,
		DisplayName = "Kubernetes pod",
		Description = "A shell in a Kubernetes pod, through kubectl exec on this computer. The machine's address names the pod.",
		QuickConnectExample = "pod name",
		DefaultPort = LocalProtocolProvider.NoPort,
		UsesPort = false,
		Capabilities = ProtocolCapabilities.Terminal,

		// kubectl runs as the user running this app, with the credentials its kubeconfig names.
		AuthenticationMethods = [AuthenticationMethod.Anonymous],
		RequiresUsername = false,
		Order = 72,
	};

	public async Task<IProtocolSession> ConnectAsync(ProtocolConnectContext context, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(context);
		if (!PseudoTerminalPlatform.IsSupported)
		{
			throw new ProtocolConnectException(ConnectFailure.HostUnreachable, PseudoTerminalPlatform.Unsupported);
		}

		KubernetesShellOptions options = KubernetesShellOptions.From(context.Connection.Options);
		LocalMachine machine = LocalMachine.Current();
		if (!KubernetesExecLaunch.TryTarget(context.Host.Address, options, out _, out string? problem))
		{
			throw new ProtocolConnectException(ConnectFailure.HostUnreachable, problem);
		}

		if (ContainerCli.FindKubectl(machine) is not { } kubectl)
		{
			throw new ProtocolConnectException(ConnectFailure.HostUnreachable, ContainerCli.KubectlMissing);
		}

		if (!KubernetesExecLaunch.TryBuild(context.Host.Address, options, kubectl, machine, out PseudoTerminalLaunch? launch, out string? pod, out problem))
		{
			throw new ProtocolConnectException(ConnectFailure.HostUnreachable, problem);
		}

		// A pod that is gone or not running makes kubectl exit at once with its own message on the screen, which ends
		// the session cleanly, the way any program that exits does.
		context.Status?.Report($"Opening a shell in {pod}");
		LocalSettings settings = _settings.Get<LocalSettings>().Clamped();
		return await _starter.StartAsync(
			context.SessionId,
			launch,
			KubernetesExecLaunch.DisplayName,
			ContainerCli.Kubectl,
			settings.ReportExitCode,
			context.TerminalSize,
			cancellationToken);
	}
}
