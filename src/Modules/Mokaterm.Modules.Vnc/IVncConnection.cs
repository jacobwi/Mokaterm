namespace Mokaterm.Modules.Vnc;

/// <summary>
/// The session feature behind the VNC view. The .NET side owns the socket and has already finished the RFB
/// handshake; attaching hands a page the synthetic handshake and then relays the session byte for byte.
/// </summary>
public interface IVncConnection
{
	/// <summary>What the server said about itself during the handshake.</summary>
	VncConnectionInfo Info { get; }

	/// <summary>
	/// Attaches <paramref name="sink"/> and returns the relay, which sends nothing until
	/// <see cref="IVncChannel.StartAsync"/> runs. Only one page can be attached at a time, and a page that detaches
	/// takes the RFB protocol state with it, so the next attach opens a fresh connection to the same server.
	/// Dispose the result to detach.
	/// </summary>
	ValueTask<IVncChannel> AttachAsync(IVncSink sink, CancellationToken cancellationToken = default);
}
