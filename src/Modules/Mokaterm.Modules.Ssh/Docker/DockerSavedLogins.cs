using Mokaterm.Abstractions.Containers;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Security;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// Registry logins kept in the vault, so logging in again on that machine is one click. Each is a password credential
/// of the login it was made on: private to that login (the keychain lists none of them), deleted with it, and named for
/// the registry. Nothing goes to settings, and the password is encrypted like every other one in the vault.
/// </summary>
internal sealed class DockerSavedLogins
{
	/// <summary>What the credential's name starts with; the registry follows.</summary>
	public const string NamePrefix = "Docker registry ";

	/// <summary>The notes that mark a credential as one of these, so a name typed elsewhere is never taken for one.</summary>
	public const string Marker = "mokaterm:docker-registry";

	private readonly ICredentialStore _store;

	public DockerSavedLogins(ICredentialStore store)
	{
		ArgumentNullException.ThrowIfNull(store);
		_store = store;
	}

	/// <summary>Every registry login kept for <paramref name="connectionId"/>, by registry.</summary>
	public async Task<IReadOnlyList<DockerSavedLogin>> ListAsync(Guid connectionId, CancellationToken cancellationToken)
	{
		IReadOnlyList<CredentialInfo> credentials = await _store.ListAsync(cancellationToken);
		return [.. credentials
			.Where(credential => IsOurs(credential, connectionId))
			.Select(credential => new DockerSavedLogin(credential.Id, credential.Name[NamePrefix.Length..], credential.Username ?? ""))
			.Where(login => DockerNames.RegistryProblem(login.Registry) is null && login.User.Length > 0)
			.OrderBy(login => login.Display, StringComparer.OrdinalIgnoreCase)];
	}

	/// <summary>The login kept for a registry, compared by host; null when there is none.</summary>
	public async Task<DockerSavedLogin?> FindAsync(Guid connectionId, string registry, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(registry);
		string host = DockerRegistryNames.Host(registry);
		return (await ListAsync(connectionId, cancellationToken)).FirstOrDefault(login => login.Host == host);
	}

	/// <summary>Keeps a login, replacing the one already kept for that registry.</summary>
	/// <param name="registry">As <c>docker login</c> was given it, or null for Docker Hub.</param>
	public async Task SaveAsync(Guid connectionId, string? registry, string user, SecretBuffer password, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(user);
		ArgumentNullException.ThrowIfNull(password);
		string key = registry is { Length: > 0 } named ? named.Trim() : DockerNames.DockerHubRegistry;
		DockerSavedLogin? existing = await FindAsync(connectionId, key, cancellationToken);
		CredentialInfo info = new()
		{
			Id = existing?.CredentialId ?? Guid.NewGuid(),
			Name = NamePrefix + key,
			Kind = CredentialKind.Password,
			Username = user,
			IsShared = false,
			OwnerConnectionId = connectionId,
			Notes = Marker,
		};

		// The store takes a string, as every editor's does; it is dropped as soon as the store encrypted it.
		await _store.SaveAsync(info, new CredentialSecretInput { Password = password.RevealString() }, cancellationToken);
	}

	/// <summary>The kept password, which the caller disposes.</summary>
	/// <exception cref="KeyNotFoundException">It was deleted meanwhile.</exception>
	public async Task<SecretBuffer> RevealAsync(DockerSavedLogin login, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(login);
		using CredentialSecret secret = await _store.RevealAsync(login.CredentialId, cancellationToken);
		return secret.Password?.Copy() ?? throw new KeyNotFoundException("The saved registry login holds no password.");
	}

	public Task ForgetAsync(DockerSavedLogin login, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(login);
		return _store.DeleteAsync(login.CredentialId, cancellationToken);
	}

	private static bool IsOurs(CredentialInfo credential, Guid connectionId) =>
		!credential.IsShared
		&& credential.OwnerConnectionId == connectionId
		&& credential.Kind == CredentialKind.Password
		&& string.Equals(credential.Notes, Marker, StringComparison.Ordinal)
		&& credential.Name.StartsWith(NamePrefix, StringComparison.Ordinal);
}
