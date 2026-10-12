using System.Text;
using Mokaterm.Abstractions.Containers;
using Mokaterm.Modules.Ssh.Docker;

namespace Mokaterm.Modules.Ssh.Tests.Fakes;

/// <summary>
/// A container that is this machine's own POSIX shell: <c>docker exec NAME sh -c SCRIPT sh ARG...</c> runs the script
/// locally with the same arguments, and the docker host's input script feeds it a file the test's host files hold. Every
/// call is recorded; a container of another name is not there, and <see cref="DockerSays"/> stands in for docker
/// refusing before the script starts (stopped, distroless).
/// </summary>
internal sealed class LocalContainerShell(string shell, string container, Func<string, byte[]?> hostFile) : IDockerShell
{
	// The script goes to a file through a quoted heredoc, so it arrives exactly as written; the input file is stdin.
	private const string Runner = """
		s=$(mktemp)
		cat > "$s" <<'MOKATERM_SCRIPT'
		{SCRIPT}
		MOKATERM_SCRIPT
		in=$1
		shift
		sh "$s" "$@" < "$in"
		r=$?
		rm -f "$s"
		exit $r
		""";

	private readonly List<DockerCall> _calls = [];
	private readonly Lock _lock = new();

	public bool IsElevated { get; init; }

	/// <summary>What docker answers instead of running anything, or null to run the scripts.</summary>
	public DockerOutput? DockerSays { get; set; }

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

		string line = DockerCommandLine.Build(call);
		IReadOnlyList<string> words = call.Arguments;
		byte[]? input = null;
		if (call.Program == DockerProgram.Shell)
		{
			// sh -c INPUT-SCRIPT sh FILE docker exec -i NAME sh -c SCRIPT sh ARG...
			Assert.Equal(DockerHostScripts.WithInputScript, words[0]);
			Assert.Equal(["docker", "exec", "-i"], words.Skip(3).Take(3));
			input = hostFile(words[2]);
			if (input is null)
			{
				return Output(line, 2, [], $"sh: 1: cannot open {words[2]}: No such file", call.BinaryOutput);
			}

			words = ["exec", .. words.Skip(6)];
		}

		Assert.Equal("exec", words[0]);
		if (words[1] != container)
		{
			return Output(line, 1, [], $"Error response from daemon: No such container: {words[1]}", call.BinaryOutput);
		}

		if (DockerSays is { } said)
		{
			return said;
		}

		Assert.Equal(["sh", "-c"], words.Skip(2).Take(2));
		string script = words[4];
		Assert.Equal("sh", words[5]);
		string? inputFile = null;
		try
		{
			if (input is not null)
			{
				inputFile = Path.GetTempFileName();
				await File.WriteAllBytesAsync(inputFile, input, cancellationToken);
			}

			// This file's line endings would reach sh inside the raw string, and sh reads a carriage return as part of a word.
			string runner = (PosixShellLocator.UnixPathPrefix + Runner.Replace("{SCRIPT}", script, StringComparison.Ordinal)).ReplaceLineEndings("\n");
			(int exitCode, byte[] output, string error) = await PosixShellLocator.RunAsync(
				shell,
				runner,
				[inputFile?.Replace('\\', '/') ?? "/dev/null", .. words.Skip(6)]);
			return Output(line, exitCode, output, error, call.BinaryOutput);
		}
		finally
		{
			if (inputFile is not null)
			{
				File.Delete(inputFile);
			}
		}
	}

	private static DockerOutput Output(string line, int exitCode, byte[] output, string error, bool binary) => new()
	{
		Command = line,
		ExitCode = exitCode,
		Output = binary ? "" : Encoding.UTF8.GetString(output),
		RawOutput = binary ? output : ReadOnlyMemory<byte>.Empty,
		Error = error,
	};
}
