using Mokaterm.Abstractions.Security;

namespace Mokaterm.Sessions.Tests.Fakes;

/// <summary>The scope's host verifier for a run: it answers the same every time and counts how often it was asked.</summary>
internal sealed class CountingVerifier : IHostIdentityVerifier
{
	private int _calls;

	public int Calls => Volatile.Read(ref _calls);

	public bool Answer { get; set; } = true;

	public ValueTask<bool> VerifyAsync(HostIdentity identity, CancellationToken cancellationToken = default)
	{
		_ = Interlocked.Increment(ref _calls);
		return ValueTask.FromResult(Answer);
	}
}
