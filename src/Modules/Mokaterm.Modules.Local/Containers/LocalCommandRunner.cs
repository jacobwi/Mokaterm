using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Mokaterm.Modules.Local.Pty.Windows;
using Mokaterm.Modules.Local.Shells;

namespace Mokaterm.Modules.Local.Containers;

/// <summary>
/// Runs docker.exe or kubectl.exe for a listing: no console, no input, the environment a shell here would get, a
/// deadline, and a cap on what is kept. Arguments are quoted by <see cref="WindowsCommandLine.Quote"/>, the rule both
/// programs split their command line by.
/// </summary>
internal sealed class LocalCommandRunner : ILocalCommandRunner
{
	private const int ErrorLimitBytes = 64 * 1024;

	private const int ReadBufferBytes = 16 * 1024;

	/// <summary>
	/// How long the pipes are waited for once the program is gone. A helper it started (a credential plugin) can hold
	/// them open after it exits, and that is not worth waiting out.
	/// </summary>
	private static readonly TimeSpan DrainLimit = TimeSpan.FromSeconds(2);

	public static LocalCommandRunner Instance { get; } = new();

	public async Task<LocalCommandResult> RunAsync(
		string program,
		IReadOnlyList<string> arguments,
		TimeSpan timeout,
		int outputLimitBytes,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(program);
		ArgumentNullException.ThrowIfNull(arguments);
		LocalMachine machine = LocalMachine.Current();
		ProcessStartInfo start = new()
		{
			FileName = program,
			Arguments = string.Join(' ', arguments.Select(WindowsCommandLine.Quote)),
			WorkingDirectory = machine.UserProfile,
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
		};
		start.Environment.Clear();
		foreach ((string name, string value) in LocalLaunchBuilder.InheritedEnvironment(machine))
		{
			start.Environment[name] = value;
		}

		using Process process = new() { StartInfo = start };

		// Starting a process can sit in an antivirus scan, and the caller's thread may be the circuit.
		await Task.Run(process.Start, cancellationToken);

		// A command that asks for something (a cloud login prompt) reads the end of its input instead of waiting out its time.
		process.StandardInput.Close();
		Task<(string Text, bool IsCut)> output = ReadAsync(process.StandardOutput.BaseStream, outputLimitBytes);
		Task<(string Text, bool IsCut)> error = ReadAsync(process.StandardError.BaseStream, ErrorLimitBytes);
		bool timedOut = false;
		using (CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
		{
			limit.CancelAfter(timeout);
			try
			{
				await process.WaitForExitAsync(limit.Token);
			}
			catch (OperationCanceledException)
			{
				timedOut = !cancellationToken.IsCancellationRequested;
				Stop(process);
			}
		}

		await Task.WhenAny(Task.WhenAll(output, error), Task.Delay(DrainLimit, CancellationToken.None));
		cancellationToken.ThrowIfCancellationRequested();
		(string text, bool isCut) = output.IsCompletedSuccessfully ? output.Result : ("", false);
		string errors = error.IsCompletedSuccessfully ? error.Result.Text : "";
		int? exitCode = !timedOut && process.HasExited ? process.ExitCode : null;
		return new LocalCommandResult(exitCode, text, errors, timedOut, isCut);
	}

	/// <summary>Keeps up to <paramref name="limit"/> bytes and reads the rest away, so the program never blocks on a full pipe.</summary>
	private static async Task<(string Text, bool IsCut)> ReadAsync(Stream stream, int limit)
	{
		using MemoryStream kept = new();
		byte[] buffer = new byte[ReadBufferBytes];
		bool cut = false;
		int read;
		while ((read = await stream.ReadAsync(buffer)) > 0)
		{
			int room = (int)Math.Max(0, limit - kept.Length);
			kept.Write(buffer, 0, Math.Min(read, room));
			cut |= read > room;
		}

		return (Encoding.UTF8.GetString(kept.GetBuffer(), 0, (int)kept.Length), cut);
	}

	private static void Stop(Process process)
	{
		try
		{
			process.Kill(entireProcessTree: true);
		}
		catch (InvalidOperationException)
		{
			// It exited on its own meanwhile.
		}
		catch (Win32Exception)
		{
			// Windows would not end it; the pipes close when it does.
		}
	}
}
