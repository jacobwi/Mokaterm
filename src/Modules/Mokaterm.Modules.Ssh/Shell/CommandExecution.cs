using Mokaterm.Abstractions.Commands;
using Renci.SshNet.Common;

namespace Mokaterm.Modules.Ssh.Shell;

/// <summary>
/// Runs one command over an exec channel to its end. Stdout and stderr are read side by side, so neither waits behind
/// the other, each kept up to the request's cap and read on past it; the channel is closed on every way out, which is
/// what stops a command that is still running and ends a read that is still waiting.
/// </summary>
internal static class CommandExecution
{
	/// <exception cref="RemoteCommandException">The command did not start, the connection closed under it, or a stream passed <see cref="RemoteCommand.MaxOutputBytes"/>.</exception>
	public static async Task<RemoteCommandOutcome> RunAsync(
		Func<CancellationToken, Task<IExecChannel>> start,
		RemoteCommandRequest request,
		TimeProvider time,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(start);
		ArgumentNullException.ThrowIfNull(request);
		cancellationToken.ThrowIfCancellationRequested();
		int keepBytes = Math.Max(request.OutputLimitBytes, RemoteCommandRequest.MinOutputLimitBytes);
		long startedAt = time.GetTimestamp();

		IExecChannel channel;
		try
		{
			channel = await start(cancellationToken);
		}
		catch (Exception ex) when (IsConnectionFailure(ex, cancellationToken))
		{
			throw new RemoteCommandException("The server did not start the command.", ex);
		}

		Task<CapturedOutput> output = OutputCapture.ReadAsync(channel.Output, keepBytes, RemoteCommand.MaxOutputBytes, "stdout", cancellationToken);
		Task<CapturedOutput> error = OutputCapture.ReadAsync(channel.ErrorOutput, keepBytes, RemoteCommand.MaxOutputBytes, "stderr", cancellationToken);
		Task finished = UntilAllOrFirstFailureAsync(output, error, channel.Execution);
		try
		{
			await finished.WaitAsync(cancellationToken);
			return new RemoteCommandOutcome
			{
				ExitCode = channel.ExitStatus,
				ExitSignal = channel.ExitSignal,
				Output = await output,
				Error = await error,
				Duration = time.GetElapsedTime(startedAt),
			};
		}
		catch (Exception ex) when (IsConnectionFailure(ex, cancellationToken))
		{
			throw new RemoteCommandException("The connection closed before the command finished.", ex);
		}
		finally
		{
			channel.Dispose();

			// A cancel stops only the wait: the closed channel then faults the reads, and this task with them.
			Observe(finished);
			Observe(output);
			Observe(error);
			Observe(channel.Execution);
		}
	}

	/// <summary>
	/// What the channel throws when the connection goes away under it. A cancel the caller asked for and a stream the
	/// command flooded are already what they should be and pass through.
	/// </summary>
	private static bool IsConnectionFailure(Exception exception, CancellationToken cancellationToken) => exception switch
	{
		OperationCanceledException => !cancellationToken.IsCancellationRequested,
		RemoteCommandException => false,
		_ => exception is SshException or ObjectDisposedException or IOException or InvalidOperationException,
	};

	/// <summary>Waits for every task, but gives up at the first failure: a flooded stdout must not wait for stderr to end.</summary>
	private static async Task UntilAllOrFirstFailureAsync(params Task[] tasks)
	{
		List<Task> pending = [.. tasks];
		while (pending.Count > 0)
		{
			Task finished = await Task.WhenAny(pending);
			await finished;
			_ = pending.Remove(finished);
		}
	}

	/// <summary>A read or a channel abandoned on the way out may still fault; nobody awaits it any more.</summary>
	private static void Observe(Task task)
	{
		if (task.IsCompleted)
		{
			_ = task.Exception;
			return;
		}

		_ = task.ContinueWith(
			static finished => _ = finished.Exception,
			CancellationToken.None,
			TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
			TaskScheduler.Default);
	}
}
