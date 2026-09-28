using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Security;

namespace Mokaterm.Modules.Mqtt.Tests.Fakes;

/// <summary>
/// Hands out credentials and counts how often the module asked. <paramref name="retryPasswords"/> is what a retry
/// answers with, in order, so a test can let the second attempt succeed.
/// </summary>
internal sealed class FakeCredentialSource : ICredentialSource
{
	private readonly string? _password;
	private readonly Queue<string?> _retryPasswords;

	public FakeCredentialSource(
		string? username = null,
		string? password = null,
		AuthenticationMethod method = AuthenticationMethod.Password,
		IEnumerable<string?>? retryPasswords = null)
	{
		Username = username;
		Method = method;
		_password = password;
		_retryPasswords = new Queue<string?>(retryPasswords ?? []);
	}

	public string? Username { get; }

	public AuthenticationMethod Method { get; }

	public int GetCount { get; private set; }

	public int RetryCount { get; private set; }

	public List<string> RetryReasons { get; } = [];

	public ValueTask<LoginCredentials?> GetAsync(CancellationToken cancellationToken)
	{
		GetCount++;
		return ValueTask.FromResult<LoginCredentials?>(Create(_password));
	}

	public ValueTask<LoginCredentials?> RetryAsync(string reason, CancellationToken cancellationToken)
	{
		RetryCount++;
		RetryReasons.Add(reason);
		if (!_retryPasswords.TryDequeue(out string? password))
		{
			return ValueTask.FromResult<LoginCredentials?>(null);
		}

		return ValueTask.FromResult<LoginCredentials?>(Create(password));
	}

	private LoginCredentials Create(string? password) => new()
	{
		Username = Username ?? "",
		Method = Method,
		Password = password is null ? null : SecretBuffer.FromString(password),
	};
}
