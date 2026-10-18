using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Containers;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Security;
using Mokaterm.Modules.Ssh.Admin;
using Mokaterm.Modules.Ssh.Elevation;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// Docker commands through sudo, for an account the daemon's socket does not let in. The password goes on stdin, and
/// the command and its names reach the script as positional arguments, as every elevated script in this module does.
/// A call's own stdin follows the password line behind a marker the script waits for, and its live output starts after
/// what sudo printed.
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
		CommandOutputHandler? listener = call.OnOutput is { } heard ? new ElevatedOutputGate(heard).Write : null;
		RemoteCommandRequest request = DockerShellRun.Request(call, line, listener);

		// The script skips stdin up to the marker, so the input reaches docker whether or not sudo read a password line.
		using SecretBuffer? input = call.Input is { } payload ? AdminScriptText.FrameInput(payload.Span) : null;
		RemoteCommandOutcome outcome;
		try
		{
			outcome = await DockerShellRun.WithTimeoutAsync(
				token => _runner.ExecuteAsync(script, arguments, input, request, _time, token),
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
