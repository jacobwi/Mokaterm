using Mokaterm.Abstractions.Security;

namespace Mokaterm.Sessions.Tests.Fakes;

internal sealed class FakeHostIdentityVerifier : IHostIdentityVerifier
{
	public ValueTask<bool> VerifyAsync(HostIdentity identity, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
}
