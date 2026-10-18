using System.Text;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>A registry login kept in the vault for one saved login: which registry and which user. The password stays in the vault.</summary>
/// <param name="CredentialId">The vault credential that holds the password.</param>
/// <param name="Registry">The registry as <c>docker login</c> was given it; Docker Hub's config key for Docker Hub.</param>
internal sealed record DockerSavedLogin(Guid CredentialId, string Registry, string User)
{
	public string Host => DockerRegistryNames.Host(Registry);

	public string Display => DockerRegistryNames.Display(Registry);

	// Registries and user names name a company's infrastructure and its accounts.
	private bool PrintMembers(StringBuilder builder)
	{
		builder.Append("CredentialId = ").Append(CredentialId);
		return true;
	}
}
