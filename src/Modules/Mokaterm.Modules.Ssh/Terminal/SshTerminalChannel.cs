using Mokaterm.Abstractions.Terminal;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace Mokaterm.Modules.Ssh.Terminal;

/// <summary>
/// An interactive shell over SSH.NET's <see cref="ShellStream"/>. SSH.NET only offers a blocking read, so one background
/// thread per shell reads output into a bounded queue that <see cref="ReadAsync"/> drains; cancelling a read never loses
/// output. <see cref="ShellOutput"/> keeps a server that prints faster than the views draw from filling memory.
/// </summary>
internal sealed class SshTerminalChannel : ITerminalChannel
{
	private const int ShellBufferSize = ShellOutput.ChunkBytes;

	private readonly ShellStream _shell;
	private readonly ShellOutput _output;
	private readonly SemaphoreSlim _writeLock = new(1, 1);
	private readonly TaskCompletionSource _closedByServer = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private int _disposed;

	private SshTerminalChannel(ShellStream shell)
	{
		_shell = shell;
		_shell.Closed += OnClosed;
		_output = new ShellOutput(buffer => _shell.Read(buffer, 0, buffer.Length), ShellBacklog, "Mokaterm SSH shell reader");
		_shell.DataReceived += OnDataReceived;
	}

	/// <summary>Completes once no more output will arrive: the shell closed, the connection dropped or the channel was disposed.</summary>
	public Task Ended => _output.Ended;

	/// <summary>Completes when the server closed the shell channel, as it does when the shell exits.</summary>
	public Task ClosedByServer => _closedByServer.Task;

	/// <summary>Opens a pseudo-terminal running the login shell.</summary>
	public static async Task<SshTerminalChannel> OpenAsync(SshClient client, string terminalType, TerminalSize size, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(client);
		TerminalSize initial = size.IsValid ? size : TerminalSize.Default;
		Dictionary<TerminalModes, uint> modes = new() { [TerminalModes.IUTF8] = 1 };

		// CreateShellStream opens the channel and waits for the pty and shell requests synchronously.
		Task<ShellStream> opening = Task.Run(
			() => client.CreateShellStream(
				terminalType,
				(uint)initial.Columns,
				(uint)initial.Rows,
				(uint)Math.Max(0, initial.PixelWidth),
				(uint)Math.Max(0, initial.PixelHeight),
				ShellBufferSize,
				modes),
			CancellationToken.None);

		try
		{
			return new SshTerminalChannel(await opening.WaitAsync(cancellationToken));
		}
		catch (OperationCanceledException) when (!opening.IsCompleted)
		{
			_ = opening.ContinueWith(
				static completed =>
				{
					if (completed.IsCompletedSuccessfully)
					{
						completed.GetAwaiter().GetResult().Dispose();
					}

					_ = completed.Exception;
				},
				CancellationToken.None,
				TaskContinuationOptions.None,
				TaskScheduler.Default);

			throw;
		}
	}

	public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken) => _output.ReadAsync(buffer, cancellationToken);

	public async ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
	{
		if (data.IsEmpty)
		{
			return;
		}

		await _writeLock.WaitAsync(cancellationToken);
		try
		{
			await _shell.WriteAsync(data, cancellationToken);
			await _shell.FlushAsync(cancellationToken);
		}
		catch (Exception ex) when (ex is ObjectDisposedException or SshException)
		{
			// The shell already closed; input has nowhere to go, and the session reports the close.
		}
		finally
		{
			_writeLock.Release();
		}
	}

	public ValueTask ResizeAsync(TerminalSize size, CancellationToken cancellationToken)
	{
		if (size.IsValid)
		{
			try
			{
				_shell.ChangeWindowSize((uint)size.Columns, (uint)size.Rows, (uint)Math.Max(0, size.PixelWidth), (uint)Math.Max(0, size.PixelHeight));
			}
			catch (Exception ex) when (ex is ObjectDisposedException or SshException)
			{
				// A closed shell has no window to resize.
			}
		}

		return ValueTask.CompletedTask;
	}

	public ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) == 0)
		{
			_shell.Closed -= OnClosed;
			_shell.DataReceived -= OnDataReceived;

			// Stopping first frees a message loop held back by the backlog, which closing the channel below still needs.
			_output.Stop();
			_shell.Dispose();
		}

		return ValueTask.CompletedTask;
	}

	private void OnClosed(object? sender, EventArgs e) => _closedByServer.TrySetResult();

	// SSH.NET raises this on its message loop once the data sits in the shell's buffer; waiting here is the flow control.
	private void OnDataReceived(object? sender, ShellDataEventArgs e) => _output.Throttle();

	private long ShellBacklog() => _shell.Length;
}
