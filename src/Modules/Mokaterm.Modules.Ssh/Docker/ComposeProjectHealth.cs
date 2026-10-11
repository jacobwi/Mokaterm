namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>How a whole compose project is doing: the one dot its header and its row show.</summary>
internal enum ComposeProjectHealth
{
	/// <summary>Every service runs and is healthy.</summary>
	Running,

	/// <summary>Some services run and some do not.</summary>
	Partial,

	/// <summary>Nothing runs.</summary>
	Stopped,

	/// <summary>A service is unhealthy or failed, whatever the rest are doing.</summary>
	Unhealthy,

	/// <summary>The project has no service at all that anything knows of.</summary>
	Empty,
}
