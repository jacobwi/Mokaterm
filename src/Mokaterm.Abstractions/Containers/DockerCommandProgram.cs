namespace Mokaterm.Abstractions.Containers;

/// <summary>Which program a <see cref="DockerCommand"/> runs.</summary>
public enum DockerCommandProgram
{
	/// <summary><c>docker</c>.</summary>
	Docker,

	/// <summary>The compose the manager found on the server: the <c>docker compose</c> plugin or <c>docker-compose</c>.</summary>
	Compose,
}
