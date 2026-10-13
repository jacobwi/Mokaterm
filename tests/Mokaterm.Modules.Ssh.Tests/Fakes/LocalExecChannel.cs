using System.Diagnostics;
using Mokaterm.Abstractions.Security;
using Mokaterm.Modules.Ssh.Shell;

namespace Mokaterm.Modules.Ssh.Tests.Fakes;

/// <summary>
/// An exec channel that is this machine's own POSIX shell running a command line, the way a login shell runs an exec
/// request: stdout and stderr are the process's, the exit status is its exit code, and closing the channel kills it.
/// </summary>
internal sealed class LocalExecChannel : IExecChannel
{
	private readonly Process _process;
	private readonly string _scriptFile;
	private int _disposed;

	private LocalExecChannel(Process process, string scriptFile)
	{
		_process = process;
		_scriptFile = scriptFile;
		Execution = process.WaitForExitAsync();
	}

	public Stream Output => _process.StandardOutput.BaseStream;

	public Stream ErrorOutput => _process.StandardError.BaseStream;

	public Task Execution { get; }

	public int? ExitStatus => _process.HasExited ? _process.ExitCode : null;

	public string? ExitSignal => null;

	/// <summary>
	/// Runs <paramref name="prelude"/> and then <paramref name="commandText"/> in <paramref name="shell"/>. They go to sh
	/// in a file, because stdin is the command's own: <paramref name="input"/> is written there as one line, and stdin
	/// closes either way.
	/// </summary>
	public static async Task<IExecChannel> StartAsync(string shell, string prelude, string commandText, SecretBuffer? input, CancellationToken cancellationToken)
	{
		string scriptFile = Path.GetTempFileName();
		await File.WriteAllTextAsync(scriptFile, (prelude + "\n" + commandText + "\n").ReplaceLineEndings("\n"), cancellationToken);
		ProcessStartInfo start = new(shell)
		{
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
		};

		// Git for Windows' sh takes C:/ paths; a backslash would be read as an escape.
		start.ArgumentList.Add(scriptFile.Replace('\\', '/'));
		Process process = Process.Start(start) ?? throw new InvalidOperationException("sh did not start.");
		try
		{
			if (input is not null)
			{
				byte[] line = [.. input.Span, (byte)'\n'];
				await process.StandardInput.BaseStream.WriteAsync(line, cancellationToken);
			}

			process.StandardInput.Close();
		}
		catch (IOException)
		{
			// The command ended before it read its input; its exit says why.
		}

		return new LocalExecChannel(process, scriptFile);
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		try
		{
			if (!_process.HasExited)
			{
				_process.Kill(entireProcessTree: true);
			}
		}
		catch (InvalidOperationException)
		{
			// It ended on its own meanwhile.
		}

		_process.Dispose();
		File.Delete(_scriptFile);
	}
}
