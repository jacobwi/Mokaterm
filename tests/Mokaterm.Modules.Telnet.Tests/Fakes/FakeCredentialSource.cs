using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Security;

namespace Mokaterm.Modules.Telnet.Tests.Fakes;

/// <summary>Hands out fixed credentials and counts how often the module asked for them.</summary>
internal sealed class FakeCredentialSource : ICredentialSource
{
	private readonly string? _password;

	public FakeCredentialSource(string? username = null, string? password = null, AuthenticationMethod method = AuthenticationMethod.Anonymous)
	{
		Username = username;
		Method = method;
		_password = password;
	}

	public string? Username { get; }

	public AuthenticationMethod Method { get; }

	public int GetCount { get; private set; }

	public int RetryCount { get; private set; }

	public ValueTask<LoginCredentials?> GetAsync(CancellationToken cancellationToken)
	{
		GetCount++;
		return ValueTask.FromResult<LoginCredentials?>(new LoginCredentials
		{
			Username = Username ?? "",
			Method = Method,
			Password = _password is null ? null : SecretBuffer.FromString(_password),
		});
	}

	public ValueTask<LoginCredentials?> RetryAsync(string reason, CancellationToken cancellationToken)
	{
		RetryCount++;
		return ValueTask.FromResult<LoginCredentials?>(null);
	}
}
