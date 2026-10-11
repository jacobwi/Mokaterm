namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>Which command line a call runs.</summary>
internal enum DockerProgram
{
	/// <summary><c>docker</c>.</summary>
	Docker,

	/// <summary><c>docker compose</c>, the CLI plugin.</summary>
	ComposePlugin,

	/// <summary><c>docker-compose</c>, the standalone program: the Python one (1.x) or a 2.x binary installed under the old name.</summary>
	ComposeStandalone,

	/// <summary>
	/// <c>sh -c SCRIPT sh ARG...</c>: a small script of the module's own around the docker host's files (which compose
	/// files exist, where they are). The first argument is the script and the rest are its positional arguments, so a
	/// path is never part of the script's text.
	/// </summary>
	Shell,
}
