namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>What the docker host can ask a registry with, found on the first update check of a session.</summary>
internal enum DockerRegistryTool
{
	/// <summary><c>docker buildx imagetools inspect</c>, which prints the tag's own digest.</summary>
	Buildx,

	/// <summary><c>docker manifest inspect --verbose</c>, which Docker 20.10 and later have without the experimental switch.</summary>
	Manifest,

	/// <summary>Neither works: an old client, or one with both left out.</summary>
	None,
}
