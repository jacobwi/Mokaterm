namespace Mokaterm.Abstractions.Security;

/// <summary>What <see cref="IHostIdentityVerifier"/> does with an identity, decided by <see cref="HostTrustRule"/>.</summary>
public enum HostTrustAction
{
	/// <summary>Connect without asking: the identity is trusted.</summary>
	Accept,

	/// <summary>Record the new identity and connect without asking (<see cref="HostKeyPolicy.AcceptNew"/>).</summary>
	TrustNew,

	/// <summary>Ask the user.</summary>
	Ask,

	/// <summary>Refuse without asking.</summary>
	Refuse,
}
