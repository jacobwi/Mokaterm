using Mokaterm.Abstractions.Commands;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>Docker commands as the login, one exec channel each, through the session's <see cref="IRemoteCommandFeature"/>.</summary>
internal sealed class UserDockerShell : IDockerShell
{
	private readonly IRemoteCommandFeature _commands;

	public UserDockerShell(IRemoteCommandFeature commands)
	{
		ArgumentNullException.ThrowIfNull(commands);
		_commands = commands;
	}

	public bool IsElevated => false;

	public async Task<DockerOutput> RunAsync(DockerCall call, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(call);
		string line = DockerCommandLine.Build(call);
		RemoteCommandRequest request = new() { Command = line, OutputLimitBytes = call.OutputLimitBytes };
		RemoteCommandOutcome outcome = await DockerShellRun.WithTimeoutAsync(
			token => _commands.RunAsync(request, token),
			call.Timeout,
			cancellationToken);

		return DockerShellRun.ToOutput(line, outcome, DockerShellRun.ErrorText(outcome), elevated: false);
	}
}
