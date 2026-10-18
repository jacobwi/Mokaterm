using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.FileSystem;
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

		CommandOutputHandler? listener = request.OnOutput;
		Task<CapturedOutput> output = OutputCapture.ReadAsync(channel.Output, keepBytes, RemoteCommand.MaxOutputBytes, "stdout", cancellationToken, listener, CommandStream.Output);
		Task<CapturedOutput> error = OutputCapture.ReadAsync(channel.ErrorOutput, keepBytes, RemoteCommand.MaxOutputBytes, "stderr", cancellationToken, listener, CommandStream.Error);
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
	/// Reads a started channel to its end the way <see cref="RemoteCommand.RunAsync"/> returns it: stdout whole, refused past
	/// <paramref name="maxOutputBytes"/>, and stderr side by side with it, its first <see cref="RemoteCommand.MaxErrorBytes"/>
	/// kept and the rest read and dropped, because SSH.NET holds whatever nobody reads. The channel is disposed on every way
	/// out. Failures of the connection pass through as SSH.NET threw them, for <c>RemoteFileErrors.TryMap</c>.
	/// </summary>
	/// <exception cref="RemoteFileSystemException">
	/// stdout passed <paramref name="maxOutputBytes"/> or stderr <see cref="RemoteCommand.MaxOutputBytes"/>; the command is stopped.
	/// </exception>
	public static async Task<RemoteCommandResult> ReadToEndAsync(IExecChannel channel, int maxOutputBytes, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(channel);
		Task<byte[]> output = RemoteCommand.ReadAllAsync(channel.Output, maxOutputBytes, cancellationToken);
		Task<CapturedOutput> error = ReadErrorAsync(channel.ErrorOutput, cancellationToken);
		Task finished = UntilAllOrFirstFailureAsync(output, error, channel.Execution);
		try
		{
			await finished.WaitAsync(cancellationToken);
			CapturedOutput stderr = await error;
			return new RemoteCommandResult(channel.ExitStatus, await output, stderr.ToText(), stderr.IsTruncated);
		}
		finally
		{
			channel.Dispose();
			Observe(finished);
			Observe(output);
			Observe(error);
			Observe(channel.Execution);
		}
	}

	private static async Task<CapturedOutput> ReadErrorAsync(Stream source, CancellationToken cancellationToken)
	{
		try
		{
			return await OutputCapture.ReadAsync(source, RemoteCommand.MaxErrorBytes, RemoteCommand.MaxOutputBytes, "stderr", cancellationToken);
		}
		catch (RemoteCommandException ex)
		{
			// The callers of RunAsync map file system failures; a flooded stderr is one, the way a flooded stdout is.
			throw new RemoteFileSystemException(RemoteFileErrorKind.Unknown, ex.Message, path: null, ex);
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
