using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Containers;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Modules.Ssh.Elevation;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// Docker commands through sudo, for an account the daemon's socket does not let in. The password goes on stdin, and
/// the command and its names reach the script as positional arguments, as every elevated script in this module does.
/// </summary>
internal sealed class SudoDockerShell : IDockerShell
{
	private readonly SudoRunner _runner;
	private readonly TimeProvider _time;

	public SudoDockerShell(SudoRunner runner, TimeProvider time)
	{
		ArgumentNullException.ThrowIfNull(runner);
		ArgumentNullException.ThrowIfNull(time);
		_runner = runner;
		_time = time;
	}

	public bool IsElevated => true;

	public async Task<DockerOutput> RunAsync(DockerCall call, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(call);
		string line = DockerCommandLine.Build(call);
		(string script, IReadOnlyList<string> arguments) = DockerCommandLine.ForScript(call);
		RemoteCommandRequest request = DockerShellRun.Request(call, line);
		RemoteCommandOutcome outcome;
		try
		{
			outcome = await DockerShellRun.WithTimeoutAsync(
				token => _runner.ExecuteAsync(script, arguments, null, request, _time, token),
				call.Timeout,
				cancellationToken);
		}
		catch (RemoteFileSystemException ex) when (ex.Kind == RemoteFileErrorKind.ElevationFailed)
		{
			throw new DockerCommandException(DockerFailure.ElevationFailed, ex.Message, innerException: ex);
		}

		(bool elevated, string sudoOutput, string commandError) = SudoErrors.Split(outcome.Error.ToText());
		if (!elevated)
		{
			string message = SudoErrors.Describe(SudoErrors.Classify(sudoOutput, outcome.ExitCode), _runner.Account, sudoOutput);
			throw new DockerCommandException(DockerFailure.ElevationFailed, message);
		}

		return DockerShellRun.ToOutput(line, outcome, commandError, elevated: true, call.BinaryOutput);
	}
}
