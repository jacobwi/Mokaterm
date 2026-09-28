using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Security;

namespace Mokaterm.Sessions.Tests.Fakes;

/// <summary>Keeps secrets as plain strings, which is fine for a test double and nothing else.</summary>
internal sealed class FakeCredentialStore : ICredentialStore
{
	private readonly Lock _lock = new();
	private readonly Dictionary<Guid, (CredentialInfo Info, CredentialSecretInput Secret)> _credentials = [];

	public event Action? Changed;

	/// <summary>Thrown by <see cref="SaveAsync"/> when set.</summary>
	public Exception? SaveError { get; set; }

	public CredentialInfo Add(CredentialInfo info, CredentialSecretInput secret)
	{
		lock (_lock)
		{
			_credentials[info.Id] = (info, secret);
		}

		return info;
	}

	public CredentialSecretInput? SecretOf(Guid id)
	{
		lock (_lock)
		{
			return _credentials.TryGetValue(id, out (CredentialInfo Info, CredentialSecretInput Secret) entry) ? entry.Secret : null;
		}
	}

	public ValueTask<IReadOnlyList<CredentialInfo>> ListAsync(CancellationToken cancellationToken = default)
	{
		lock (_lock)
		{
			return ValueTask.FromResult<IReadOnlyList<CredentialInfo>>([.. _credentials.Values.Select(entry => entry.Info)]);
		}
	}

	public ValueTask<CredentialInfo?> FindAsync(Guid id, CancellationToken cancellationToken = default)
	{
		lock (_lock)
		{
			return ValueTask.FromResult(_credentials.TryGetValue(id, out (CredentialInfo Info, CredentialSecretInput Secret) entry) ? entry.Info : null);
		}
	}

	public ValueTask<CredentialInfo> SaveAsync(CredentialInfo info, CredentialSecretInput? secret, CancellationToken cancellationToken = default)
	{
		if (SaveError is not null)
		{
			return ValueTask.FromException<CredentialInfo>(SaveError);
		}

		lock (_lock)
		{
			CredentialSecretInput stored = secret ?? (_credentials.TryGetValue(info.Id, out (CredentialInfo Info, CredentialSecretInput Secret) existing) ? existing.Secret : new CredentialSecretInput());
			_credentials[info.Id] = (info, stored);
		}

		Changed?.Invoke();
		return ValueTask.FromResult(info);
	}

	public ValueTask<CredentialSecret> RevealAsync(Guid id, CancellationToken cancellationToken = default)
	{
		lock (_lock)
		{
			if (!_credentials.TryGetValue(id, out (CredentialInfo Info, CredentialSecretInput Secret) entry))
			{
				return ValueTask.FromException<CredentialSecret>(new KeyNotFoundException($"No credential {id}."));
			}

			return ValueTask.FromResult(new CredentialSecret(
				entry.Info.Kind,
				ToBuffer(entry.Secret.Password),
				ToBuffer(entry.Secret.PrivateKey),
				ToBuffer(entry.Secret.Passphrase),
				ToBuffer(entry.Secret.ProxyPassword)));
		}
	}

	public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
	{
		lock (_lock)
		{
			_ = _credentials.Remove(id);
		}

		Changed?.Invoke();
		return Task.CompletedTask;
	}

	private static SecretBuffer? ToBuffer(string? value) => value is null ? null : SecretBuffer.FromString(value);
}
