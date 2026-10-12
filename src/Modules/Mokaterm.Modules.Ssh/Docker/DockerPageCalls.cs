using Mokaterm.Abstractions.Containers;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// How a <see cref="DockerCommand"/> from a page of another module becomes a <see cref="DockerCall"/>: compose is the one
/// the manager picked, the folder passes <see cref="DockerNames.PathProblem"/>, and the time and output limits are the
/// manager's own for a read or a change.
/// </summary>
internal static class DockerPageCalls
{
	/// <exception cref="ArgumentException">A word holds NUL, the folder is not an absolute path, or the command is compose and there is none.</exception>
	public static DockerCall ToCall(DockerCommand command, ComposeTool? compose)
	{
		ArgumentNullException.ThrowIfNull(command);
		ArgumentNullException.ThrowIfNull(command.Arguments);
		DockerProgram program = command.Program switch
		{
			DockerCommandProgram.Docker => DockerProgram.Docker,
			DockerCommandProgram.Compose => compose?.Program
				?? throw new ArgumentException("Compose is not installed on this docker host.", nameof(command)),
			_ => throw new ArgumentOutOfRangeException(nameof(command), command.Program, "There is no such program."),
		};

		if (command.Arguments.Any(argument => argument is null || argument.Contains('\0', StringComparison.Ordinal)))
		{
			throw new ArgumentException("A command line cannot carry a missing word or a NUL character.", nameof(command));
		}

		string? directory = command.WorkingDirectory is { } folder
			? DockerCommands.Checked(folder, DockerNames.PathProblem(folder, "working directory"))
			: null;
		TimeSpan? timeout = command.Timeout is { } asked
			? asked < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : asked > DockerCall.LongTimeout ? DockerCall.LongTimeout : asked
			: null;
		DockerCall call = new()
		{
			Program = program,
			Arguments = [.. command.Arguments],
			WorkingDirectory = directory,
			Timeout = timeout ?? DockerCall.DefaultTimeout,
		};

		return command.IsChange ? call.AsChange(timeout) : call;
	}
}
