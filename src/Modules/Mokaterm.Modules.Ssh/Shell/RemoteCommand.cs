using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Security;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace Mokaterm.Modules.Ssh.Shell;

/// <summary>The outcome of a command that ran to completion.</summary>
internal sealed record RemoteCommandResult(int? ExitStatus, byte[] Output, string Error);

/// <summary>A running exec channel with its stdout available as a stream.</summary>
internal sealed class RemoteCommand : IDisposable
{
	/// <summary>
	/// The most stdout <see cref="RunAsync"/> collects. Listings and account databases stay far below it; a server that
	/// sends more is broken or hostile, and holding it all would exhaust memory.
	/// </summary>
	internal const int MaxOutputBytes = 64 * 1024 * 1024;

	private const int CopyBufferBytes = 80 * 1024;

	private readonly SshCommand _command;

	private RemoteCommand(SshCommand command, Task execution)
	{
		_command = command;
		Execution = execution;
	}

	/// <summary>Stdout. Reads return 0 once the command finished and its output was drained.</summary>
	public Stream Output => _command.OutputStream;

	/// <summary>Completes when the channel closes; faults when the connection drops, cancels with the token.</summary>
	public Task Execution { get; }

	public int? ExitStatus => _command.ExitStatus;

	/// <summary>Everything the command wrote to stderr. Blocks until the command finished, so read it after <see cref="Execution"/>.</summary>
	public string ReadError() => _command.Error;

	/// <summary>
	/// Starts <paramref name="commandText"/> and, when <paramref name="input"/> is given, writes it as one line on stdin and
	/// closes stdin, so sudo asking twice fails instead of waiting. SSH.NET only sends EOF after data was written, so a
	/// command started without input must never read stdin.
	/// </summary>
	public static async Task<RemoteCommand> StartAsync(SshClient client, string commandText, SecretBuffer? input, CancellationToken cancellationToken)
	{
		SshCommand command = client.CreateCommand(commandText);
		try
		{
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

			return new RemoteCommand(command, execution);
		}
		catch
		{
			command.Dispose();
			throw;
		}
	}

	/// <summary>Runs a command to completion and collects its output.</summary>
	/// <exception cref="RemoteFileSystemException">The command wrote more than <see cref="MaxOutputBytes"/>; it is stopped.</exception>
	public static async Task<RemoteCommandResult> RunAsync(SshClient client, string commandText, SecretBuffer? input, CancellationToken cancellationToken)
	{
		using RemoteCommand command = await StartAsync(client, commandText, input, cancellationToken);
		byte[] output = await ReadAllAsync(command.Output, MaxOutputBytes, cancellationToken);
		await command.Execution;
		return new RemoteCommandResult(command.ExitStatus, output, command.ReadError());
	}

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
					throw new RemoteFileSystemException(
						RemoteFileErrorKind.Unknown,
						string.Create(CultureInfo.CurrentCulture, $"The server sent more than {maxBytes / (1024 * 1024)} MB for one command, so it was stopped."));
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
		_command.Dispose();

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
