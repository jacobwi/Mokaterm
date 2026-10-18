using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Containers;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// Docker commands as the login, one exec channel each, through the session's <see cref="IRemoteCommandFeature"/>. A
/// call's stdin goes to docker as it is, since nothing else reads it first.
/// </summary>
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
		RemoteCommandRequest request = DockerShellRun.Request(call, line, call.OnOutput) with { Input = call.Input };
		RemoteCommandOutcome outcome = await DockerShellRun.WithTimeoutAsync(
			token => _commands.RunAsync(request, token),
			call.Timeout,
			cancellationToken);

		return DockerShellRun.ToOutput(line, outcome, outcome.Error.ToText(), elevated: false, call.BinaryOutput);
	}
}
