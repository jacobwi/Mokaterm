namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// Everything a compose command needs to find its project again: the compose to run, the project's name (null for a
/// file found on disk, whose name compose works out itself), its folder, its files, and the env files it was started
/// with when its labels say so, since <c>up</c> without them would read a different <c>.env</c>.
/// </summary>
internal sealed record ComposeTarget(
	ComposeTool Tool,
	string? Project,
	string? WorkingDirectory,
	IReadOnlyList<string> ConfigFiles,
	IReadOnlyList<string> EnvironmentFiles)
{
	public static ComposeTarget For(ComposeTool tool, ComposeProject project)
	{
		ArgumentNullException.ThrowIfNull(tool);
		ArgumentNullException.ThrowIfNull(project);
		return new ComposeTarget(tool, project.Name, project.WorkingDirectory, project.ConfigFiles, project.EnvironmentFiles);
	}

	/// <summary>A compose file found on disk that runs as no project yet: its folder is the project's folder.</summary>
	public static ComposeTarget ForFile(ComposeTool tool, string file)
	{
		ArgumentNullException.ThrowIfNull(tool);
		ArgumentNullException.ThrowIfNull(file);
		return new ComposeTarget(tool, null, DockerHostPaths.DirectoryOf(file), [file], []);
	}

	/// <summary>True when compose can find the project's files, which <c>up</c>, <c>pull</c> and <c>config</c> need.</summary>
	public bool HasFiles => ConfigFiles.Count > 0 || WorkingDirectory is not null;
}
