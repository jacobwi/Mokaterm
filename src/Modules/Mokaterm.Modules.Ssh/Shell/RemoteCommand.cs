using System.Buffers;
using System.Security.Cryptography;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Security;
using Mokaterm.UI.Common.Formatting;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace Mokaterm.Modules.Ssh.Shell;

/// <summary>The outcome of a command that ran to completion.</summary>
/// <param name="Error">The start of stderr, at most <see cref="RemoteCommand.MaxErrorBytes"/> of it.</param>
/// <param name="ErrorTruncated">stderr held more than <paramref name="Error"/>; the rest was read and dropped.</param>
internal sealed record RemoteCommandResult(int? ExitStatus, byte[] Output, string Error, bool ErrorTruncated = false);

/// <summary>A running exec channel with its stdout available as a stream.</summary>
internal sealed class RemoteCommand : IExecChannel
{
	/// <summary>
	/// The most stdout <see cref="RunAsync"/> collects, and the most anything here reads from either stream before it
	/// stops the command. Listings and account databases stay far below it; a server that sends more is broken or
	/// hostile, and holding it all would exhaust memory.
	/// </summary>
	internal const int MaxOutputBytes = 64 * 1024 * 1024;

	/// <summary>
	/// The most stderr <see cref="RunAsync"/> keeps. Its stderr only ever becomes a message, which quotes a line or two;
	/// the rest is still read, because SSH.NET holds whatever nobody reads, and dropped.
	/// </summary>
	internal const int MaxErrorBytes = 1024 * 1024;

	internal const int CopyBufferBytes = 80 * 1024;

	private readonly SshCommand _command;
	private readonly ExecSlot _slot;

	private RemoteCommand(SshCommand command, Task execution, ExecSlot slot)
	{
		_command = command;
		Execution = execution;
		_slot = slot;
	}

	/// <summary>Stdout. Reads return 0 once the command finished and its output was drained.</summary>
	public Stream Output => _command.OutputStream;

	/// <summary>Completes when the channel closes; faults when the connection drops, cancels with the token.</summary>
	public Task Execution { get; }

	public int? ExitStatus => _command.ExitStatus;

	/// <summary>
	/// Stderr as a stream. Read it alongside <see cref="Output"/> and to its end: SSH.NET's own <c>Error</c> would collect
	/// all of it into one string, however much the server sent.
	/// </summary>
	public Stream ErrorOutput => _command.ExtendedOutputStream;

	public string? ExitSignal => _command.ExitSignal;

	/// <summary>
	/// Closes stdin without writing anything, so a command that reads it sees the end of its input instead of waiting
	/// for a line that will never come.
	/// </summary>
	public void CloseInput()
	{
		Stream stdin;
		try
		{
			stdin = _command.CreateInputStream();
		}
		catch (Exception ex) when (ex is InvalidOperationException or SshException or ObjectDisposedException)
		{
			// The command already finished, so nothing will read stdin.
			return;
		}

		CloseInput(stdin);
	}

	/// <summary>
	/// Starts <paramref name="commandText"/> and, when <paramref name="input"/> is given, writes it as one line on stdin and
	/// closes stdin, so sudo asking twice fails instead of waiting. SSH.NET only sends EOF after data was written, so a
	/// command started without input must never read stdin.
	/// </summary>
	/// <param name="runsLong">
	/// The channel may stay open for minutes (<see cref="ExecChannelBudget.RunsLong"/>), which keeps it out of the slots
	/// the connection's budget holds back for short calls.
	/// </param>
	/// <remarks>
	/// The channel waits for a slot in its connection's <see cref="ExecChannelBudget"/> first and holds it until it is
	/// disposed. Every exec channel of this module opens here, which is what makes the budget the connection's.
	/// </remarks>
	public static async Task<RemoteCommand> StartAsync(SshClient client, string commandText, SecretBuffer? input, bool runsLong, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(client);
		ExecSlot slot = await ExecChannelBudget.For(client).AcquireAsync(runsLong, cancellationToken);
		SshCommand? created = null;
		try
		{
			SshCommand command = client.CreateCommand(commandText);
			created = command;

			// ExecuteAsync opens the channel and sends the exec request synchronously before it returns its task.
			Task execution = await Task.Factory.StartNew(
				() => command.ExecuteAsync(cancellationToken),
				cancellationToken,
				TaskCreationOptions.DenyChildAttach,
				TaskScheduler.Default);

			if (input is not null)
			{
				await SendLineAsync(command, input, cancellationToken);
			}

			return new RemoteCommand(command, execution, slot);
		}
		catch
		{
			created?.Dispose();
			slot.Dispose();
			throw;
		}
	}

	/// <summary>
	/// Runs a command to completion and collects its output: stdout whole, stderr up to <see cref="MaxErrorBytes"/>
	/// (<see cref="CommandExecution.ReadToEndAsync"/>).
	/// </summary>
	/// <exception cref="RemoteFileSystemException">The command wrote more than <see cref="MaxOutputBytes"/> to either stream; it is stopped.</exception>
	public static async Task<RemoteCommandResult> RunAsync(SshClient client, string commandText, SecretBuffer? input, bool runsLong, CancellationToken cancellationToken) =>
		await CommandExecution.ReadToEndAsync(await StartAsync(client, commandText, input, runsLong, cancellationToken), MaxOutputBytes, cancellationToken);

	/// <summary>Reads <paramref name="source"/> to its end, refusing to hold more than <paramref name="maxBytes"/>.</summary>
	/// <exception cref="RemoteFileSystemException">The stream holds more than <paramref name="maxBytes"/>.</exception>
	internal static async Task<byte[]> ReadAllAsync(Stream source, int maxBytes, CancellationToken cancellationToken)
	{
		using MemoryStream output = new();
		byte[] buffer = ArrayPool<byte>.Shared.Rent(CopyBufferBytes);
		try
		{
			int read;
			while ((read = await source.ReadAsync(buffer.AsMemory(0, CopyBufferBytes), cancellationToken)) > 0)
			{
				if (output.Length + read > maxBytes)
				{
					// A piece of a file is capped well under a megabyte at first, so the size is not always whole megabytes.
					throw new RemoteFileSystemException(
						RemoteFileErrorKind.Unknown,
						$"The server sent more than {DisplayFormat.Bytes(maxBytes)} for one command, so it was stopped.");
				}

				await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
			}

			return output.ToArray();
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(buffer);
		}
	}

	public void Dispose()
	{
		try
		{
			_command.Dispose();
		}
		finally
		{
			_slot.Dispose();
		}

		// Disposing a running command faults its task; nobody awaits it any more.
		if (Execution.IsFaulted)
		{
			_ = Execution.Exception;
		}
	}

	private static async Task SendLineAsync(SshCommand command, SecretBuffer input, CancellationToken cancellationToken)
	{
		Stream stdin;
		try
		{
			stdin = command.CreateInputStream();
		}
		catch (InvalidOperationException)
		{
			// The command already finished, so nothing will read stdin.
			return;
		}

		// A pinned array wiped here: Stream.Write(ReadOnlySpan) would copy the secret into a pooled buffer.
		byte[] line = GC.AllocateArray<byte>(input.Length + 1, pinned: true);
		try
		{
			input.Span.CopyTo(line);
			line[^1] = (byte)'\n';
			await stdin.WriteAsync(line, cancellationToken);
		}
		catch (Exception ex) when (ex is SshException or ObjectDisposedException or InvalidOperationException)
		{
			// The channel closed early; the command's exit status and stderr explain why.
		}
		finally
		{
			CryptographicOperations.ZeroMemory(line);
			CloseInput(stdin);
		}
	}

	private static void CloseInput(Stream stdin)
	{
		try
		{
			stdin.Dispose();
		}
		catch (Exception ex) when (ex is SshException or ObjectDisposedException or InvalidOperationException)
		{
			// Closing stdin of a channel that is already closed has nothing left to do.
		}
	}
}
