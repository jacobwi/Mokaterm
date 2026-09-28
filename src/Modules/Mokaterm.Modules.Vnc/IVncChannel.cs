namespace Mokaterm.Modules.Vnc;

/// <summary>
/// An attached relay. Disposing it detaches the page and ends the connection it was serving; the session reports
/// the end of a connection through its own state, so a view has nothing else to watch here.
/// </summary>
public interface IVncChannel : IAsyncDisposable
{
	/// <summary>
	/// Sends the synthetic greeting, which is what makes the page start its handshake. Call it once the page's
	/// answers can reach <see cref="SendAsync"/>, because the first one arrives while this is still running.
	/// </summary>
	ValueTask StartAsync(CancellationToken cancellationToken = default);

	/// <summary>Sends bytes the page produced. The handshake bytes are answered locally and never reach the server.</summary>
	ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);
}
