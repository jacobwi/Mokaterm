namespace Mokaterm.Modules.Ssh.Components.Docker;

/// <summary>
/// The ids the Docker manager's own pages register under. Pages open each other by these, and a plugin page that names
/// one opens it the same way; changing one breaks both.
/// </summary>
internal static class DockerPageIds
{
	public const string Overview = "overview";

	public const string Containers = "containers";

	public const string Stats = "stats";

	public const string Compose = "compose";

	public const string Images = "images";

	public const string Volumes = "volumes";

	public const string Networks = "networks";

	public const string Cleanup = "cleanup";
}
