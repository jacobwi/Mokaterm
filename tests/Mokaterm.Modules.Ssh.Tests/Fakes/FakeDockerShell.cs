using Mokaterm.Abstractions.Containers;
using Mokaterm.Modules.Ssh.Docker;

namespace Mokaterm.Modules.Ssh.Tests.Fakes;

/// <summary>
/// A server's docker as a table: each command line (exactly as the confirmation would show it) answers with an exit
/// and both streams. Anything not in the table is a command the shell cannot find, which is what a host without that
/// program says. Every line asked for is recorded.
/// </summary>
internal sealed class FakeDockerShell : IDockerShell
{
	private readonly Dictionary<string, DockerOutput> _answers = new(StringComparer.Ordinal);
	private readonly List<string> _asked = [];
	private readonly Lock _lock = new();

	public bool IsElevated { get; init; }

	public IReadOnlyList<string> Asked
	{
		get
		{
			lock (_lock)
			{
				return [.. _asked];
			}
		}
	}

	public FakeDockerShell Answer(string commandLine, int exitCode, string output = "", string error = "")
	{
		_answers[commandLine] = new DockerOutput { Command = commandLine, ExitCode = exitCode, Output = output, Error = error };
		return this;
	}

	public Task<DockerOutput> RunAsync(DockerCall call, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(call);
		string line = DockerCommandLine.Build(call);
		lock (_lock)
		{
			_asked.Add(line);
		}

		DockerOutput answer = _answers.TryGetValue(line, out DockerOutput? found)
			? found
			: new DockerOutput { Command = line, ExitCode = DockerErrors.CommandNotFoundExitStatus, Error = $"sh: 1: {call.ProgramWords[0]}: not found" };

		// A call that asked for bytes gets them as the real shells hand them over: a fresh copy, never decoded.
		return Task.FromResult(call.BinaryOutput ? answer with { Output = "", RawOutput = System.Text.Encoding.UTF8.GetBytes(answer.Output) } : answer);
	}

	public DockerClient Client(string host = "build-server") => new(this, host);
}
