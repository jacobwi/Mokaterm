using System.Text;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// One registry the client config knows: its key, where its credentials are kept, and nothing of the credentials
/// themselves. Read by <see cref="DockerClientConfigReader"/>, which never keeps an <c>auth</c> value.
/// </summary>
internal sealed record DockerRegistryEntry
{
	/// <summary>The key exactly as the config has it, which is what <c>docker logout</c> is given.</summary>
	public required string Key { get; init; }

	/// <summary>Listed under <c>auths</c>: docker logged in to it, its credentials in the file or in the store.</summary>
	public bool LoggedIn { get; init; }

	/// <summary>The file itself holds a credential for it (an <c>auth</c> or a token), which is base64 and not encrypted.</summary>
	public bool StoredInFile { get; init; }

	/// <summary>The credential helper <c>credHelpers</c> names for it, without its <c>docker-credential-</c> prefix.</summary>
	public string? Helper { get; init; }

	/// <summary>The host it is compared by (<see cref="DockerRegistryNames.Host"/>).</summary>
	public string Host => DockerRegistryNames.Host(Key);

	public string Display => DockerRegistryNames.Display(Key);

	// A registry names a company's infrastructure, which is never logged.
	private bool PrintMembers(StringBuilder builder)
	{
		builder.Append("LoggedIn = ").Append(LoggedIn).Append(", StoredInFile = ").Append(StoredInFile).Append(", Helper = ").Append(Helper is not null);
		return true;
	}
}
