using Mokaterm.Abstractions.Containers;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// Registries as the client config keys them and as image references name them, which are not the same text: Docker Hub
/// is <c>https://index.docker.io/v1/</c> in the config and <c>docker.io</c> in a reference, and a key may carry a scheme
/// and a path where a reference has neither. <see cref="Host"/> is the one form both are compared in.
/// </summary>
internal static class DockerRegistryNames
{
	public const string DockerHubName = "Docker Hub";

	/// <summary>
	/// The registry's host, lower case, with its port and without a scheme or a path; <c>docker.io</c> for every name Docker
	/// Hub goes by. The same form <see cref="DockerImageName.Registry"/> takes, so a key and a reference can be compared.
	/// </summary>
	public static string Host(string registry)
	{
		ArgumentNullException.ThrowIfNull(registry);
		string text = registry.Trim();
		int scheme = text.IndexOf("://", StringComparison.Ordinal);
		if (scheme >= 0)
		{
			text = text[(scheme + 3)..];
		}

		int slash = text.IndexOf('/', StringComparison.Ordinal);
		if (slash >= 0)
		{
			text = text[..slash];
		}

		text = text.ToLowerInvariant();
		return text is "index.docker.io" or "registry-1.docker.io" or "registry.hub.docker.com" or "" ? DockerImageName.DefaultRegistry : text;
	}

	/// <summary>True for Docker Hub under any of its names.</summary>
	public static bool IsDockerHub(string registry) => Host(registry) == DockerImageName.DefaultRegistry;

	/// <summary>What a list shows for a registry: "Docker Hub", or its host.</summary>
	public static string Display(string registry) => IsDockerHub(registry) ? DockerHubName : Host(registry);

	/// <summary>
	/// The registry a login form's text names, checked: empty for Docker Hub (which <c>docker login</c> picks itself when
	/// none is named), otherwise the text as typed.
	/// </summary>
	/// <exception cref="ArgumentException">The text is not a registry.</exception>
	public static string? ForLogin(string? typed)
	{
		string text = typed?.Trim() ?? "";
		if (text.Length == 0)
		{
			return null;
		}

		return DockerCommands.Checked(text, DockerNames.RegistryProblem(text));
	}
}
