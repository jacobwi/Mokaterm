namespace Mokaterm.Abstractions.Security;

/// <summary>
/// Stops a connect deadline while the user is asked about a certificate, and starts it again with the answer.
/// Reading a fingerprint takes longer than any sensible handshake timeout. VNC hands one to its handshake and RDP
/// asks it between TLS and the login.
/// </summary>
public sealed class DeadlinePausingVerifier : IHostIdentityVerifier
{
	private readonly IHostIdentityVerifier _inner;
	private readonly CancellationTokenSource _deadline;
	private readonly TimeSpan _timeout;
	private readonly CancellationToken _userToken;

	/// <param name="inner">The verifier that decides, and asks when it has to.</param>
	/// <param name="deadline">The connect deadline, which this pauses and restarts.</param>
	/// <param name="timeout">What the deadline starts again with once the answer is in.</param>
	/// <param name="userToken">The user's own cancellation, which the prompt follows instead of the deadline.</param>
	public DeadlinePausingVerifier(IHostIdentityVerifier inner, CancellationTokenSource deadline, TimeSpan timeout, CancellationToken userToken)
	{
		ArgumentNullException.ThrowIfNull(inner);
		ArgumentNullException.ThrowIfNull(deadline);
		_inner = inner;
		_deadline = deadline;
		_timeout = timeout;
		_userToken = userToken;
	}

	public async ValueTask<bool> VerifyAsync(HostIdentity identity, CancellationToken cancellationToken = default)
	{
		_deadline.CancelAfter(Timeout.InfiniteTimeSpan);
		try
		{
			// The prompt follows the user's own cancellation, not the handshake deadline.
			return await _inner.VerifyAsync(identity, _userToken);
		}
		finally
		{
			if (!_deadline.IsCancellationRequested)
			{
				_deadline.CancelAfter(_timeout);
			}
		}
	}
}
