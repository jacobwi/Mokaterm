using System.Text;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Containers;
using Mokaterm.Modules.Ssh.Docker;
using Mokaterm.Modules.Ssh.Shell;
using Mokaterm.Modules.Ssh.Tests.Fakes;

namespace Mokaterm.Modules.Ssh.Tests.Search;

/// <summary>
/// A container that is this machine's own POSIX shell, for scripts run with <c>docker exec NAME sh -c SCRIPT sh ARG...</c>
/// and nothing else. Unlike <see cref="LocalContainerShell"/> the arguments reach sh inside a file, as they reach a server
/// inside the exec request: Git for Windows' sh would glob a <c>*</c> in an argument it was started with, and every
/// search argument holds one. Every call is recorded.
/// </summary>
internal sealed class ScriptedContainer(string shell, string container) : IDockerShell
{
	private readonly List<DockerCall> _calls = [];
	private readonly Lock _lock = new();

	public bool IsElevated => false;

	public IReadOnlyList<DockerCall> Calls
	{
		get
		{
			lock (_lock)
			{
				return [.. _calls];
			}
		}
	}

	public async Task<DockerOutput> RunAsync(DockerCall call, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(call);
		lock (_lock)
		{
			_calls.Add(call);
		}

		IReadOnlyList<string> words = call.Arguments;
		Assert.Equal(DockerProgram.Docker, call.Program);
		Assert.Equal(["exec", container, "sh", "-c"], words.Take(4));
		Assert.Equal("sh", words[5]);
		string command = PosixShell.ScriptCommand(words[4], words.Skip(6));
		IExecChannel channel = await LocalExecChannel.StartAsync(shell, PosixShellLocator.UnixPathPrefix, command, input: null, cancellationToken);
		RemoteCommandResult result = await CommandExecution.ReadToEndAsync(channel, call.OutputLimitBytes, cancellationToken);
		return new DockerOutput
		{
			Command = DockerCommandLine.Build(call),
			ExitCode = result.ExitStatus,
			Output = call.BinaryOutput ? "" : Encoding.UTF8.GetString(result.Output),
			RawOutput = call.BinaryOutput ? result.Output : ReadOnlyMemory<byte>.Empty,
			Error = result.Error,
		};
	}
}
