using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Modules.Local.Pty;
using Mokaterm.Modules.Local.Sessions;
using Mokaterm.Modules.Local.Shells;

namespace Mokaterm.Modules.Local.Containers;

/// <summary>
/// Opens a shell in a Docker container through this computer's docker.exe, in a pseudo console like a local shell.
/// docker.exe does the talking to the daemon, so a context or a host that reaches another machine works the way it does
/// in a terminal, and its own credential helpers and SSH config apply.
/// </summary>
internal sealed class DockerShellProvider : IProtocolProvider
{
	public const string ProtocolId = "docker";

	private readonly ISettingsService _settings;
	private readonly LocalSessionStarter _starter;

	public DockerShellProvider(ISettingsService settings, ILoggerFactory loggerFactory)
	{
		_settings = settings;
		_starter = new LocalSessionStarter(loggerFactory);
	}

	public ProtocolDescriptor Descriptor { get; } = new()
	{
		Id = ProtocolId,
		DisplayName = "Docker container",
		Description = "A shell in a Docker container, through docker exec on this computer. The machine's address names the container.",
		QuickConnectExample = "container name",
		DefaultPort = LocalProtocolProvider.NoPort,
		UsesPort = false,
		Capabilities = ProtocolCapabilities.Terminal,

		// docker.exe runs as the user running this app and talks to the daemon with that user's access.
		AuthenticationMethods = [AuthenticationMethod.Anonymous],
		RequiresUsername = false,
		Order = 71,
	};

	public async Task<IProtocolSession> ConnectAsync(ProtocolConnectContext context, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(context);
		if (!PseudoTerminalPlatform.IsSupported)
		{
			throw new ProtocolConnectException(ConnectFailure.HostUnreachable, PseudoTerminalPlatform.Unsupported);
		}

		DockerShellOptions options = DockerShellOptions.From(context.Connection.Options);
		LocalMachine machine = LocalMachine.Current();

		// The options are checked before the program is looked for, so a mistake in them is reported on any computer.
		if (!DockerExecLaunch.TryTarget(context.Host.Address, options, out _, out string? problem))
		{
			throw new ProtocolConnectException(ConnectFailure.HostUnreachable, problem);
		}

		if (ContainerCli.FindDocker(machine) is not { } docker)
		{
			throw new ProtocolConnectException(ConnectFailure.HostUnreachable, ContainerCli.DockerMissing);
		}

		if (!DockerExecLaunch.TryBuild(
			context.Host.Address,
			options,
			docker,
			machine,
			LocalSessionStarter.TerminalType(_settings),
			out PseudoTerminalLaunch? launch,
			out string? container,
			out problem))
		{
			throw new ProtocolConnectException(ConnectFailure.HostUnreachable, problem);
		}

		// A container that is not running makes docker exit at once with its own message on the screen, which ends the
		// session cleanly, the way any program that exits does.
		context.Status?.Report($"Opening a shell in {container}");
		LocalSettings settings = _settings.Get<LocalSettings>().Clamped();
		return await _starter.StartAsync(
			context.SessionId,
			launch,
			DockerExecLaunch.DisplayName,
			ContainerCli.Docker,
			settings.ReportExitCode,
			context.TerminalSize,
			cancellationToken);
	}
}
