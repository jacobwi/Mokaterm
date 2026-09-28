namespace Mokaterm.Abstractions.Terminal;

/// <summary>
/// The byte stream behind an interactive terminal (an SSH shell today; Telnet, serial or a local PTY later).
/// Implemented by protocol modules and exposed as a session feature. Output is UTF-8 with VT escape sequences.
/// </summary>
public interface ITerminalChannel : IAsyncDisposable
{
	/// <summary>
	/// Reads the next chunk of output into <paramref name="buffer"/>. Returns 0 once the remote side closed
	/// the channel. Only one read runs at a time.
	/// </summary>
	ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken);

	/// <summary>Sends keyboard input. Safe to call while a read is pending.</summary>
	ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken);

	/// <summary>Tells the remote pseudo-terminal the new window size.</summary>
	ValueTask ResizeAsync(TerminalSize size, CancellationToken cancellationToken);
}
