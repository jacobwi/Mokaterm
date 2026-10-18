namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>Where a <see cref="DockerLiveRun"/> is.</summary>
internal enum DockerLiveState
{
	Running,

	/// <summary>Every call exited 0.</summary>
	Succeeded,

	/// <summary>A call exited with anything else, or ran out of time on the server.</summary>
	Failed,

	/// <summary>The user stopped it.</summary>
	Stopped,

	/// <summary>It never ran to its end: the connection went, sudo refused, or this side's own time ran out.</summary>
	Lost,
}
