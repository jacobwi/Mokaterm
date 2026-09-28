namespace Mokaterm.Modules.Rdp;

/// <summary>
/// The session feature behind the RDP view. The .NET side owns the socket, the TLS channel, the decoder and the
/// input state; a page attaches to be painted and to report what the user does.
/// </summary>
public interface IRdpConnection
{
	/// <summary>What the server told us about itself, kept current across desktop resizes.</summary>
	RdpConnectionInfo Info { get; }

	/// <summary>
	/// Attaches <paramref name="sink"/> and returns the channel. Only one page can be attached at a time; a second
	/// one replaces the first. Dispose the result to detach, which leaves the session running.
	/// </summary>
	ValueTask<IRdpChannel> AttachAsync(IRdpSink sink, CancellationToken cancellationToken = default);
}
