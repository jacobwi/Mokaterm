using System.Collections.Concurrent;
using Mokaterm.Abstractions.Security;

namespace Mokaterm.Tests.Shared;

/// <summary>Records every identity it is asked about and answers through <see cref="Decide"/>.</summary>
internal sealed class FakeHostVerifier : IHostIdentityVerifier
{
	public Func<HostIdentity, CancellationToken, Task<bool>> Decide { get; init; } = static (_, _) => Task.FromResult(true);

	public ConcurrentQueue<HostIdentity> Seen { get; } = new();

	public static FakeHostVerifier Accepting() => new();

	public static FakeHostVerifier Rejecting() => new() { Decide = static (_, _) => Task.FromResult(false) };

	/// <summary>Answers after <paramref name="delay"/>, like a user reading a prompt.</summary>
	public static FakeHostVerifier AnsweringAfter(TimeSpan delay, bool answer) => new()
	{
		Decide = async (_, cancellationToken) =>
		{
			await Task.Delay(delay, cancellationToken);
			return answer;
		},
	};

	public async ValueTask<bool> VerifyAsync(HostIdentity identity, CancellationToken cancellationToken = default)
	{
		Seen.Enqueue(identity);
		return await Decide(identity, cancellationToken);
	}
}
