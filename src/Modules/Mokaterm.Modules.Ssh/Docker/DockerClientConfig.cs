using System.Text;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// What the docker client on the host says about registries, from its <c>config.json</c>: whose file it is, the
/// registries it is logged in to, the credential helpers it uses. Never a credential: those stay in the file.
/// </summary>
internal sealed record DockerClientConfig
{
	/// <summary>The account the file belongs to: the login, or <c>root</c> when the manager runs through sudo.</summary>
	public required string User { get; init; }

	/// <summary>The file docker reads (<c>$DOCKER_CONFIG/config.json</c>, else <c>~/.docker/config.json</c>).</summary>
	public required string Path { get; init; }

	/// <summary>False when there is no such file yet, which means docker never logged in anywhere for this account.</summary>
	public bool Exists { get; init; }

	/// <summary>
	/// The store every registry's credentials go to when no helper is named for it (<c>credsStore</c>), such as
	/// <c>pass</c> or <c>secretservice</c>; null when they go into the file itself.
	/// </summary>
	public string? CredentialsStore { get; init; }

	/// <summary>Every registry under <c>auths</c> or <c>credHelpers</c>, in the file's order, each once.</summary>
	public IReadOnlyList<DockerRegistryEntry> Registries { get; init; } = [];

	/// <summary>What the client says for a registry, compared by host; null when it knows nothing of it.</summary>
	public DockerRegistryEntry? Find(string registry)
	{
		ArgumentNullException.ThrowIfNull(registry);
		string host = DockerRegistryNames.Host(registry);
		return Registries.FirstOrDefault(entry => entry.Host == host && entry.LoggedIn)
			?? Registries.FirstOrDefault(entry => entry.Host == host);
	}

	/// <summary>The helper a login to <paramref name="registry"/> stores its credentials with, or null for the file itself.</summary>
	public string? HelperFor(string registry) => Find(registry)?.Helper ?? CredentialsStore;

	// Paths and registries name the server's layout and a company's infrastructure.
	private bool PrintMembers(StringBuilder builder)
	{
		builder.Append("Exists = ").Append(Exists).Append(", Registries = ").Append(Registries.Count).Append(", CredentialsStore = ").Append(CredentialsStore is not null);
		return true;
	}
}
