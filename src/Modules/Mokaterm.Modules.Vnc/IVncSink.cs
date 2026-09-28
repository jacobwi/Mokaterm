namespace Mokaterm.Modules.Vnc;

/// <summary>
/// Receives the bytes a VNC session sends to its page. Implemented by the view, which forwards them to noVNC.
/// </summary>
public interface IVncSink
{
	/// <summary>
	/// Delivers bytes. The relay awaits this before it reads more from the socket, so a slow page pushes back on the
	/// server instead of filling memory. The buffer is reused as soon as the call returns: copy what you keep.
	/// </summary>
	ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken);
}
