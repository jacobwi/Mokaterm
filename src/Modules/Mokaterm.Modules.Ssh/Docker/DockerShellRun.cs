using System.Globalization;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Containers;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Modules.Ssh.Shell;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>What both shells do around a run: the call's time limit, the failures that mean "it never finished", and the text.</summary>
internal static class DockerShellRun
{
	/// <exception cref="DockerCommandException">The time ran out or the connection went away.</exception>
	public static async Task<RemoteCommandOutcome> WithTimeoutAsync(
		Func<CancellationToken, Task<RemoteCommandOutcome>> run,
		TimeSpan timeout,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(run);
		using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		limit.CancelAfter(timeout);
		try
		{
			return await run(limit.Token);
		}
		catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
		{
			throw new DockerCommandException(
				DockerFailure.ConnectionLost,
				string.Create(CultureInfo.CurrentCulture, $"docker did not finish within {Describe(timeout)}, so it was stopped."),
				innerException: ex);
		}
		catch (Exception ex) when (ex is RemoteCommandException or RemoteFileSystemException)
		{
			throw new DockerCommandException(DockerFailure.ConnectionLost, ex.Message, innerException: ex);
		}
	}

	/// <summary>
	/// The request a call runs under: its output cap, its live output listener, and long when its time limit is (a pull,
	/// a build) or when it says so itself (<see cref="DockerCall.RunsLong"/>). The call's stdin is left to each shell,
	/// since sudo needs it framed behind its own password line.
	/// </summary>
	public static RemoteCommandRequest Request(DockerCall call, string commandLine, CommandOutputHandler? onOutput = null)
	{
		ArgumentNullException.ThrowIfNull(call);
		return new RemoteCommandRequest
		{
			Command = commandLine,
			OutputLimitBytes = call.OutputLimitBytes,
			RunsLong = call.RunsLong || ExecChannelBudget.RunsLong(call.Timeout),
			OnOutput = onOutput,
		};
	}

	/// <param name="binary">The call's <see cref="DockerCall.BinaryOutput"/>: stdout stays bytes.</param>
	public static DockerOutput ToOutput(string command, RemoteCommandOutcome outcome, string error, bool elevated, bool binary = false)
	{
		ArgumentNullException.ThrowIfNull(outcome);
		return new DockerOutput
		{
			Command = elevated ? "sudo " + command : command,
			ExitCode = outcome.ExitCode,
			ExitSignal = outcome.ExitSignal,
			Output = binary ? "" : outcome.Output.ToText(),
			RawOutput = binary ? outcome.Output.Bytes : ReadOnlyMemory<byte>.Empty,
			OutputTruncated = outcome.Output.IsTruncated,
			Error = error,
			ErrorTruncated = outcome.Error.IsTruncated,
			IsElevated = elevated,
			Duration = outcome.Duration,
		};
	}

	private static string Describe(TimeSpan timeout) => timeout.TotalMinutes >= 1
		? string.Create(CultureInfo.CurrentCulture, $"{timeout.TotalMinutes:0} min")
		: string.Create(CultureInfo.CurrentCulture, $"{timeout.TotalSeconds:0} s");
}
