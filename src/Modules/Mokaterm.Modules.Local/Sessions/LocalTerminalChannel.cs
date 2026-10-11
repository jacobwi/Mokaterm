using System.Globalization;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.Modules.Local.Pty;

namespace Mokaterm.Modules.Local.Sessions;

/// <summary>
/// A terminal over a shell on this computer. The pseudo terminal already speaks what the view does (UTF-8 with VT
/// sequences both ways), so bytes pass through untouched: no line ending to translate, no echo to draw.
/// </summary>
internal sealed class LocalTerminalChannel : ITerminalChannel, IPseudoTerminalOutput
{
	private const char Escape = (char)0x1B;

	private readonly TerminalOutputQueue _output = new();
	private readonly TaskCompletionSource _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly string _programName;
	private readonly bool _reportExitCode;
	private readonly ILogger _logger;
	private IPseudoTerminal? _terminal;
	private int _disposed;

	/// <param name="programName">What the exit notice calls the shell, such as <c>PowerShell</c>.</param>
	/// <param name="reportExitCode">Write a notice into the terminal when the shell exits with anything but 0.</param>
	public LocalTerminalChannel(string programName, bool reportExitCode, ILogger logger)
	{
		_programName = programName;
		_reportExitCode = reportExitCode;
		_logger = logger;
	}

	/// <summary>Completes once no more output will arrive: the shell exited, its console went away or this was disposed.</summary>
	public Task Ended => _ended.Task;

	/// <summary>Why the shell ended other than by exiting, or null.</summary>
	public Exception? Failure { get; private set; }

	/// <summary>The shell's exit code once it has exited on its own.</summary>
	public int? ExitCode { get; private set; }

	/// <summary>
	/// Hands over the pseudo terminal this channel is the output of. Called right after the terminal starts, which may
	/// already have written something, and that is queued here meanwhile.
	/// </summary>
	public void Attach(IPseudoTerminal terminal)
	{
		ArgumentNullException.ThrowIfNull(terminal);
		_terminal = terminal;
		_ = WatchAsync(terminal);
	}

	public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken) =>
		_output.ReadAsync(buffer, cancellationToken);

	public ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken) =>
		Volatile.Read(ref _disposed) != 0 || _terminal is not { } terminal
			? ValueTask.CompletedTask
			: terminal.WriteAsync(data, cancellationToken);

	public ValueTask ResizeAsync(TerminalSize size, CancellationToken cancellationToken)
	{
		if (Volatile.Read(ref _disposed) == 0)
		{
			_terminal?.Resize(size);
		}

		return ValueTask.CompletedTask;
	}

	/// <summary>
	/// Runs on the terminal's reader thread, which is where blocking belongs: a full queue holds the reader, the reader
	/// holds conhost, and conhost holds the shell, so a view that falls behind slows the shell instead of growing a buffer.
	/// </summary>
	bool IPseudoTerminalOutput.Write(ReadOnlySpan<byte> chunk)
	{
		if (Volatile.Read(ref _disposed) != 0)
		{
			return false;
		}

		try
		{
			_output.WriteAsync(chunk.ToArray(), CancellationToken.None).AsTask().GetAwaiter().GetResult();
			return true;
		}
		catch (ChannelClosedException)
		{
			return false;
		}
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		// The queue goes first, so the terminal's reader stops waiting on a view and drains the console instead, which is
		// what lets the console close.
		_output.Complete();
		_ended.TrySetResult();
		if (_terminal is { } terminal)
		{
			await terminal.DisposeAsync();
		}
	}

	private async Task WatchAsync(IPseudoTerminal terminal)
	{
		int? exitCode = await terminal.Exited;
		if (Volatile.Read(ref _disposed) != 0)
		{
			return;
		}

		ExitCode = exitCode;
		if (exitCode is not { } code)
		{
			Failure = new IOException($"The console closed under {_programName} while it was still running.");
			_logger.LogInformation("The console of a local shell closed under it.");
		}
		else if (code != 0 && _reportExitCode)
		{
			await _output.WriteIfOpenAsync(ExitNotice(code), CancellationToken.None);
		}

		_output.Complete();
		_ended.TrySetResult();
	}

	/// <summary>
	/// A line in the shape of the session's own notices. Codes past a short, and negative ones, are NTSTATUS values,
	/// which are only recognisable in hex (0xC000013A is a Ctrl+C).
	/// </summary>
	private byte[] ExitNotice(int code)
	{
		string written = code is < 0 or > 0xFFFF
			? string.Create(CultureInfo.InvariantCulture, $"{code} (0x{unchecked((uint)code):X8})")
			: code.ToString(CultureInfo.InvariantCulture);
		return Encoding.UTF8.GetBytes($"\r\n{Escape}[2m[mokaterm] {_programName} exited with code {written}{Escape}[0m\r\n");
	}
}
