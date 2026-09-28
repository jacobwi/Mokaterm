namespace Mokaterm.Modules.Rdp;

/// <summary>
/// Receives what an RDP session produces. Implemented by the view, which paints the frames and sets the cursor.
/// Every call is awaited before the session makes the next one, so a slow page slows the frame rate instead of
/// queueing work without limit.
/// </summary>
public interface IRdpSink
{
	/// <summary>
	/// One changed region, as <see cref="Protocol.RdpFrame"/> writes it: a small header followed by raw RGBA or a
	/// PNG. The buffer is reused as soon as the call returns: copy what you keep.
	/// </summary>
	ValueTask FrameAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken);

	/// <summary>The server drew a new pointer.</summary>
	ValueTask PointerImageAsync(RdpPointerImage cursor, CancellationToken cancellationToken);

	/// <summary>The server hid the pointer or asked for the default one.</summary>
	ValueTask PointerStyleAsync(bool hidden, CancellationToken cancellationToken);

	/// <summary>The server moved the pointer itself, which the page follows so both sides agree where it is.</summary>
	ValueTask PointerMovedAsync(int x, int y, CancellationToken cancellationToken);

	/// <summary>The desktop changed size, which happens after the server accepts a resize.</summary>
	ValueTask DesktopResizedAsync(int width, int height, CancellationToken cancellationToken);
}
