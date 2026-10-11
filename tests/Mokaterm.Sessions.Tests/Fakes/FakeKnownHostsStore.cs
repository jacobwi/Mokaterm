using Mokaterm.Abstractions.Security;

namespace Mokaterm.Sessions.Tests.Fakes;

/// <summary>Answers every identity with <see cref="Match"/>.</summary>
internal sealed class FakeKnownHostsStore : IKnownHostsStore
{
	public event Action? Changed
	{
		add { }
		remove { }
	}

	public HostIdentityMatch Match { get; set; } = HostIdentityMatch.Trusted;

	public ValueTask<IReadOnlyList<KnownHost>> ListAsync(CancellationToken cancellationToken = default) =>
		ValueTask.FromResult<IReadOnlyList<KnownHost>>([]);

	public ValueTask<HostIdentityMatch> MatchAsync(HostIdentity identity, CancellationToken cancellationToken = default) =>
		ValueTask.FromResult(Match);

	public Task TrustAsync(HostIdentity identity, CancellationToken cancellationToken = default) => Task.CompletedTask;

	public Task RemoveAsync(KnownHost entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
