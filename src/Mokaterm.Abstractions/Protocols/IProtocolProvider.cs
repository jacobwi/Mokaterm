namespace Mokaterm.Abstractions.Protocols;

/// <summary>
/// Opens sessions for one protocol. Providers are singletons: everything scoped to a user or to a
/// connection attempt arrives through <see cref="ProtocolConnectContext"/>.
/// </summary>
public interface IProtocolProvider
{
	ProtocolDescriptor Descriptor { get; }

	/// <summary>
	/// Connects and authenticates. Report progress through <see cref="ProtocolConnectContext.Status"/>, get
	/// secrets through <see cref="ProtocolConnectContext.Credentials"/>, verify the server through
	/// <see cref="ProtocolConnectContext.HostVerifier"/>, and honour cancellation at every network wait.
	/// </summary>
	/// <exception cref="ProtocolConnectException">Connecting or authenticating failed in a way the user should see.</exception>
	Task<IProtocolSession> ConnectAsync(ProtocolConnectContext context, CancellationToken cancellationToken);
}
