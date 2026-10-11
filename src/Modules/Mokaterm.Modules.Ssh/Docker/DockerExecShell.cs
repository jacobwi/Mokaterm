namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>What runs a command typed into a container's command box.</summary>
public enum DockerExecShell
{
	/// <summary><c>sh -c COMMAND</c>: pipes, variables and globs work, as at a prompt.</summary>
	Sh,

	/// <summary><c>bash -c COMMAND</c>, for an image that has bash.</summary>
	Bash,

	/// <summary>The command split into words and run directly, for an image with no shell at all.</summary>
	None,
}
