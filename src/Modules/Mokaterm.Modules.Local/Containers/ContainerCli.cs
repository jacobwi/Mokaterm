using Mokaterm.Modules.Local.Shells;

namespace Mokaterm.Modules.Local.Containers;

/// <summary>
/// Finds docker.exe and kubectl.exe by reading, never by running anything: PATH first, which is what the user gets
/// typing the name in a shell, then the folders the desktop tools install into without always putting them on PATH.
/// </summary>
internal static class ContainerCli
{
	public const string Docker = "docker";

	public const string Kubectl = "kubectl";

	public const string DockerMissing =
		"docker.exe was not found on this computer. Install Docker Desktop, Rancher Desktop or the Docker CLI, then connect again.";

	public const string KubectlMissing =
		"kubectl.exe was not found on this computer. Install kubectl (winget install Kubernetes.kubectl), or Docker Desktop or Rancher Desktop, which ship it, then connect again.";

	public static string? FindDocker(LocalMachine machine) => Find(Docker, InstallFolders(machine), machine);

	public static string? FindKubectl(LocalMachine machine) => Find(Kubectl, InstallFolders(machine), machine);

	/// <summary>
	/// Where Docker Desktop, Rancher Desktop and winget put both tools, for all users and for one. Docker Desktop's
	/// per-user install sits under Programs\DockerDesktop, and winget links land in WinGet\Links, which is only on PATH
	/// for shells started after the install.
	/// </summary>
	internal static IEnumerable<string> InstallFolders(LocalMachine machine)
	{
		ArgumentNullException.ThrowIfNull(machine);
		if (machine.Variable("ProgramFiles") is { } programFiles)
		{
			yield return Path.Combine(programFiles, "Docker", "Docker", "resources", "bin");
			yield return Path.Combine(programFiles, "Rancher Desktop", "resources", "resources", "win32", "bin");
		}

		if (machine.Variable("LOCALAPPDATA") is { } localAppData)
		{
			yield return Path.Combine(localAppData, "Programs", "DockerDesktop", "resources", "bin");
			yield return Path.Combine(localAppData, "Programs", "Rancher Desktop", "resources", "resources", "win32", "bin");
			yield return Path.Combine(localAppData, "Microsoft", "WinGet", "Links");
		}
	}

	private static string? Find(string name, IEnumerable<string> folders, LocalMachine machine) =>
		LocalProgramPath.FindOnPath(name, machine)
			?? folders
				.Where(Path.IsPathFullyQualified)
				.Select(folder => Path.Combine(folder, name + ".exe"))
				.FirstOrDefault(machine.FileExists);
}
