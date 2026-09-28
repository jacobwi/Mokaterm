using Mokaterm.Abstractions.Security;

namespace Mokaterm.Modules.Ftp.Connection;

/// <summary>The verifier's answer for one certificate, which may still be waiting on the user.</summary>
internal sealed class FtpCertificateDecision
{
	public FtpCertificateDecision(HostIdentity identity, Task<bool> verification)
	{
		Identity = identity;
		Verification = verification;
	}

	public HostIdentity Identity { get; }

	/// <summary>Completes with the answer. Never faults: verifier failures count as a rejection.</summary>
	public Task<bool> Verification { get; }

	public bool IsDecided => Verification.IsCompleted;

	/// <summary>True when the decision is made and the certificate was accepted.</summary>
	public bool IsApproved => Verification.IsCompletedSuccessfully && Verification.GetAwaiter().GetResult();

	// SslStream's certificate callback is synchronous, so a bounded blocking wait is the only way to accept an already
	// trusted certificate within the same handshake.

	/// <summary>Blocks for at most <paramref name="timeout"/>. Returns false while the answer is still open.</summary>
	public bool Wait(TimeSpan timeout) => Verification.Wait(timeout);

	public Task<bool> WaitAsync(CancellationToken cancellationToken) => Verification.WaitAsync(cancellationToken);
}
