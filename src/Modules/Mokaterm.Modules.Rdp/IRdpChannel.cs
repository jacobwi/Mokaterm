namespace Mokaterm.Modules.Rdp;

/// <summary>
/// An attached page. Disposing it detaches: the session keeps running and keeps its picture of the desktop, so
/// attaching again repaints from what it already has instead of reconnecting.
/// </summary>
public interface IRdpChannel : IAsyncDisposable
{
	/// <summary>Paints the whole desktop once, which is what a page needs before the first update reaches it.</summary>
	ValueTask StartAsync(CancellationToken cancellationToken = default);

	/// <summary>Sends what the user did. Events are applied in order and dropped while the channel is view only.</summary>
	ValueTask SendInputAsync(IReadOnlyList<RdpInputEvent> events, CancellationToken cancellationToken = default);

	/// <summary>
	/// Stops sending input. Turning it on releases every key the session is holding down, so nothing stays pressed
	/// on the server.
	/// </summary>
	ValueTask SetViewOnlyAsync(bool viewOnly, CancellationToken cancellationToken = default);

	/// <summary>
	/// Asks the server for a desktop of this size. False when the server did not offer the dynamic resize channel,
	/// in which case the negotiated size stays and the view scales it.
	/// </summary>
	ValueTask<bool> ResizeAsync(int width, int height, CancellationToken cancellationToken = default);
}
