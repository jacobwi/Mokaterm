namespace Mokaterm.Abstractions.Terminal;

/// <summary>
/// A session-lifetime pump over an <see cref="ITerminalChannel"/>. It keeps reading while no view is
/// attached, holds a bounded replay buffer so a remounted view can catch up, and survives reconnects:
/// the session manager binds the same stream to the new channel.
/// </summary>
public interface ITerminalStream
{
	/// <summary>The last size sent to the remote side.</summary>
	TerminalSize Size { get; }

	/// <summary>False once the channel ended, until a reconnect binds a new one.</summary>
	bool IsOpen { get; }

	/// <summary>Raised when <see cref="IsOpen"/> changes. Handlers may run on any thread.</summary>
	event Action? StateChanged;

	/// <summary>
	/// Attaches a view. The replay buffer is written to <paramref name="sink"/> first, then live output.
	/// Dispose the result to detach.
	/// </summary>
	IDisposable Attach(ITerminalSink sink);

	/// <summary>Sends input. Dropped silently while the stream is closed.</summary>
	ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);

	/// <summary>Sends text as UTF-8.</summary>
	ValueTask SendTextAsync(string text, CancellationToken cancellationToken = default);

	ValueTask ResizeAsync(TerminalSize size, CancellationToken cancellationToken = default);

	/// <summary>
	/// Writes a local notice into the output (for example "connection closed") as if it came from the remote
	/// side, so every attached view and the replay buffer show it.
	/// </summary>
	ValueTask WriteLocalAsync(string text, CancellationToken cancellationToken = default);
}

/// <summary>Receives terminal output. Implemented by views and by anything that records sessions.</summary>
public interface ITerminalSink
{
	/// <summary>
	/// Delivers output. The pump awaits this before reading more, so a slow view pushes back on the remote
	/// side instead of buffering without limit.
	/// </summary>
	ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken);
}
