using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Security;

namespace Mokaterm.Modules.Ssh.Tests.Fakes;

/// <summary>A saved password login with no retries.</summary>
internal sealed class PasswordCredentialSource(string username, string password) : ICredentialSource
{
	public string? Username => username;

	public AuthenticationMethod Method => AuthenticationMethod.Password;

	public ValueTask<LoginCredentials?> GetAsync(CancellationToken cancellationToken) =>
		ValueTask.FromResult<LoginCredentials?>(new LoginCredentials
		{
			Username = username,
			Method = AuthenticationMethod.Password,
			Password = SecretBuffer.FromString(password),
		});

	public ValueTask<LoginCredentials?> RetryAsync(string reason, CancellationToken cancellationToken) =>
		ValueTask.FromResult<LoginCredentials?>(null);
}
