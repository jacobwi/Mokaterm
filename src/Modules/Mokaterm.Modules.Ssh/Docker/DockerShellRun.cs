using System.Globalization;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.FileSystem;

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

	public static string OutputText(RemoteCommandOutcome outcome)
	{
		ArgumentNullException.ThrowIfNull(outcome);
		return DockerText.Decode(outcome.Output.Bytes.Span, outcome.Output.IsTruncated);
	}

	public static string ErrorText(RemoteCommandOutcome outcome)
	{
		ArgumentNullException.ThrowIfNull(outcome);
		return DockerText.Decode(outcome.Error.Bytes.Span, outcome.Error.IsTruncated);
	}

	public static DockerOutput ToOutput(string command, RemoteCommandOutcome outcome, string error, bool elevated)
	{
		ArgumentNullException.ThrowIfNull(outcome);
		return new DockerOutput
		{
			Command = elevated ? "sudo " + command : command,
			ExitCode = outcome.ExitCode,
			ExitSignal = outcome.ExitSignal,
			Output = OutputText(outcome),
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
