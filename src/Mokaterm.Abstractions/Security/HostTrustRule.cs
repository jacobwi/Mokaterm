namespace Mokaterm.Abstractions.Security;

/// <summary>
/// The host key policy as one decision. The verifier acts on it, and a command run that skips hosts that would ask uses
/// it to tell beforehand whether a host's identity would put a question on screen.
/// </summary>
public static class HostTrustRule
{
	/// <summary>
	/// What happens to <paramref name="identity"/>, which compared to the known hosts as <paramref name="match"/>, under
	/// <paramref name="policy"/>. An identity accepted once for the window is the verifier's own business and is not
	/// covered here.
	/// </summary>
	public static HostTrustAction Decide(HostIdentity identity, HostIdentityMatch match, HostKeyPolicy policy)
	{
		ArgumentNullException.ThrowIfNull(identity);
		return match switch
		{
			HostIdentityMatch.Trusted => HostTrustAction.Accept,

			// A certificate the operating system already trusts for this name needs no second opinion.
			HostIdentityMatch.Unknown when identity is { Kind: HostIdentityKind.TlsCertificate, ChainTrusted: true } => HostTrustAction.Accept,
			HostIdentityMatch.Unknown => policy switch
			{
				HostKeyPolicy.Strict => HostTrustAction.Refuse,
				HostKeyPolicy.AcceptNew => HostTrustAction.TrustNew,
				_ => HostTrustAction.Ask,
			},

			// A changed identity is never trusted quietly: only the policy that asks about everything asks about this.
			_ => policy is HostKeyPolicy.Strict or HostKeyPolicy.AcceptNew ? HostTrustAction.Refuse : HostTrustAction.Ask,
		};
	}
}
