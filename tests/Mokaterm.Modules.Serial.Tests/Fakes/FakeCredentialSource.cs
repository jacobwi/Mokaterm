using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Credentials;

namespace Mokaterm.Modules.Serial.Tests.Fakes;

/// <summary>A serial port is opened, not logged in to: the module must never ask for credentials.</summary>
internal sealed class FakeCredentialSource : ICredentialSource
{
	public string? Username => null;

	public AuthenticationMethod Method => AuthenticationMethod.Anonymous;

	public int GetCount { get; private set; }

	public ValueTask<LoginCredentials?> GetAsync(CancellationToken cancellationToken)
	{
		GetCount++;
		return ValueTask.FromResult<LoginCredentials?>(new LoginCredentials
		{
			Username = "",
			Method = AuthenticationMethod.Anonymous,
		});
	}

	public ValueTask<LoginCredentials?> RetryAsync(string reason, CancellationToken cancellationToken) =>
		throw new InvalidOperationException("A serial session has nothing to retry a login with.");
}
