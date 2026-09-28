using Mokaterm.Abstractions.Security;

namespace Mokaterm.Modules.Vnc.Protocol;

/// <summary>
/// Stops the connect deadline while the user is asked about a certificate, and starts it again with the answer.
/// Reading a fingerprint takes longer than any sensible handshake timeout.
/// </summary>
internal sealed class DeadlinePausingVerifier : IHostIdentityVerifier
{
	private readonly IHostIdentityVerifier _inner;
	private readonly CancellationTokenSource _deadline;
	private readonly TimeSpan _timeout;
	private readonly CancellationToken _userToken;

	public DeadlinePausingVerifier(IHostIdentityVerifier inner, CancellationTokenSource deadline, TimeSpan timeout, CancellationToken userToken)
	{
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
