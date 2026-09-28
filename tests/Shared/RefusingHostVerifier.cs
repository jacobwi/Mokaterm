using Mokaterm.Abstractions.Security;

namespace Mokaterm.Tests.Shared;

/// <summary>
/// A verifier that must never be asked, for a protocol with nothing to identify a host by: a serial port, plain
/// telnet, an MQTT connection without TLS.
/// </summary>
internal sealed class RefusingHostVerifier : IHostIdentityVerifier
{
	public ValueTask<bool> VerifyAsync(HostIdentity identity, CancellationToken cancellationToken = default) =>
		throw new InvalidOperationException("This connection carries no host identity to verify.");
}
