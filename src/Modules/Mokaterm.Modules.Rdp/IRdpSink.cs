namespace Mokaterm.Modules.Rdp;

/// <summary>
/// Receives what an RDP session produces. Implemented by the view, which paints the frames and sets the cursor.
/// Every call is awaited before the session makes the next one, so a slow page slows the frame rate instead of
/// queueing work without limit. All of them come from the session's one frame loop, never from the code that reads the
/// connection or sends input, so a page that takes its time holds up nothing but its own picture.
/// </summary>
public interface IRdpSink
{
	/// <summary>
	/// One changed region, as <see cref="Protocol.RdpFrameEncoder"/> writes it: a small header followed by raw RGBA or a
	/// PNG. The array is the sink's: the session made it for this call and never touches it again.
	/// </summary>
	ValueTask FrameAsync(byte[] frame, CancellationToken cancellationToken);

	/// <summary>The server drew a new pointer.</summary>
	ValueTask PointerImageAsync(RdpPointerImage cursor, CancellationToken cancellationToken);

	/// <summary>The server hid the pointer or asked for the default one.</summary>
	ValueTask PointerStyleAsync(bool hidden, CancellationToken cancellationToken);

	/// <summary>The server moved the pointer itself, which the page follows so both sides agree where it is.</summary>
	ValueTask PointerMovedAsync(int x, int y, CancellationToken cancellationToken);

	/// <summary>
	/// The desktop has this size. Comes before the first frame a page gets and before the first frame after the server
	/// accepted a resize, so no frame is ever painted on a canvas of the wrong size.
	/// </summary>
	ValueTask DesktopResizedAsync(int width, int height, CancellationToken cancellationToken);
}
