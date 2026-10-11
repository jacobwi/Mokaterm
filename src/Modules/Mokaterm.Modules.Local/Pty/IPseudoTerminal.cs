using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.Modules.Local.Pty;

/// <summary>
/// A program running in a pseudo terminal on this computer: ConPTY on Windows today, and the place a forkpty one goes
/// for macOS. Output is delivered to the <see cref="IPseudoTerminalOutput"/> it was started with.
/// </summary>
/// <remarks>
/// Disposing ends the program and everything it started, and returns once the terminal has let go of all of it.
/// </remarks>
internal interface IPseudoTerminal : IAsyncDisposable
{
	int ProcessId { get; }

	/// <summary>
	/// Completes once the terminal has closed and every byte the program wrote has reached the output: with the exit
	/// code when the program ended on its own, or null when it was still running, because the terminal was disposed or
	/// the console went away under it.
	/// </summary>
	Task<int?> Exited { get; }

	/// <summary>Sends keyboard input. Safe to call from any thread; writes after the terminal closed are dropped.</summary>
	ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken);

	/// <summary>Tells the console its new size. Does nothing once it is closing.</summary>
	void Resize(TerminalSize size);
}
