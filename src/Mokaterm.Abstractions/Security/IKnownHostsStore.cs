namespace Mokaterm.Abstractions.Security;

/// <summary>Trusted SSH host keys and TLS certificates, encrypted with the vault.</summary>
public interface IKnownHostsStore
{
	event Action? Changed;

	ValueTask<IReadOnlyList<KnownHost>> ListAsync(CancellationToken cancellationToken = default);

	/// <summary>Compares against entries with the same host, port, kind and algorithm.</summary>
	ValueTask<HostIdentityMatch> MatchAsync(HostIdentity identity, CancellationToken cancellationToken = default);

	/// <summary>Records the identity, replacing an entry with the same host, port, kind and algorithm.</summary>
	Task TrustAsync(HostIdentity identity, CancellationToken cancellationToken = default);

	Task RemoveAsync(KnownHost entry, CancellationToken cancellationToken = default);
}

/// <summary>
/// Decides whether to continue connecting to a server: checks known hosts, applies <see cref="HostKeyPolicy"/>,
/// asks the user when the policy says so, and records the answer. "Accept once" answers last for the UI scope.
/// </summary>
public interface IHostIdentityVerifier
{
	ValueTask<bool> VerifyAsync(HostIdentity identity, CancellationToken cancellationToken = default);
}
