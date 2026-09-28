using System.Collections.Concurrent;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Security;

namespace Mokaterm.Modules.Ftp.Tests.Fakes;

/// <summary>Answers the first login with fixed credentials and each retry with the next queued password, or null when none is left.</summary>
internal sealed class FakeCredentialSource : ICredentialSource
{
	private readonly string? _password;
	private readonly ConcurrentQueue<string> _retryPasswords;

	public FakeCredentialSource(string? username, string? password, AuthenticationMethod method = AuthenticationMethod.Password, params string[] retryPasswords)
	{
		Username = username;
		Method = method;
		_password = password;
		_retryPasswords = new ConcurrentQueue<string>(retryPasswords);
	}

	public string? Username { get; }

	public AuthenticationMethod Method { get; }

	public int GetCount { get; private set; }

	public ConcurrentQueue<string> RetryReasons { get; } = new();

	public ValueTask<LoginCredentials?> GetAsync(CancellationToken cancellationToken)
	{
		GetCount++;
		return ValueTask.FromResult<LoginCredentials?>(Create(_password));
	}

	public ValueTask<LoginCredentials?> RetryAsync(string reason, CancellationToken cancellationToken)
	{
		RetryReasons.Enqueue(reason);
		return ValueTask.FromResult(_retryPasswords.TryDequeue(out string? password) ? Create(password) : null);
	}

	private LoginCredentials Create(string? password) => new()
	{
		Username = Username ?? "",
		Method = Method,
		Password = password is null ? null : SecretBuffer.FromString(password),
	};
}
