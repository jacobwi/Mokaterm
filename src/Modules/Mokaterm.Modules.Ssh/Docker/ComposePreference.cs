namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>Which compose a session's commands use, chosen in the manager and kept for the session.</summary>
public enum ComposePreference
{
	/// <summary>The plugin when the server has it, else the standalone program.</summary>
	Auto,

	/// <summary>Always <c>docker compose</c>.</summary>
	Plugin,

	/// <summary>Always <c>docker-compose</c>.</summary>
	Standalone,
}
