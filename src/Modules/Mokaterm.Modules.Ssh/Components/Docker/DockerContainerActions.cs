using Mokaterm.Modules.Ssh.Docker;

namespace Mokaterm.Modules.Ssh.Components.Docker;

/// <summary>
/// What each menu action on a container runs, and what it asks first: one table for the list's rows and the panel, so
/// both say the same thing before a stop, a kill or a remove. Names go on the command lines rather than ids, because
/// they are what the user reads in the confirmation and would type.
/// </summary>
internal static class DockerContainerActions
{
	/// <summary>The change behind <paramref name="action"/>, or null for one that changes nothing (logs, copy).</summary>
	/// <param name="alive">True while the container runs, is paused or restarts: a remove then has to kill it first.</param>
	public static DockerChangePlan? Plan(string action, string name, bool alive) => action switch
	{
		"start" => new DockerChangePlan(DockerCommands.Start(name), $"Start {name}"),
		"restart" => new DockerChangePlan(DockerCommands.Restart(name), $"Restart {name}"),
		"pause" => new DockerChangePlan(DockerCommands.Pause(name), $"Pause {name}"),
		"unpause" => new DockerChangePlan(DockerCommands.Unpause(name), $"Unpause {name}"),
		"stop" => new DockerChangePlan(
			DockerCommands.Stop(name),
			$"Stop {name}",
			$"{name} gets SIGTERM and, if it is still running ten seconds later, SIGKILL.",
			"Stop"),
		"kill" => new DockerChangePlan(
			DockerCommands.Kill(name),
			$"Kill {name}",
			$"{name} gets SIGKILL at once: nothing in it gets to save its work or clean up.",
			"Kill"),
		"remove" or "remove-volumes" => Remove(name, alive, action == "remove-volumes"),
		_ => null,
	};

	private static DockerChangePlan Remove(string name, bool alive, bool volumes)
	{
		string message = $"{name} and its writable layer are deleted"
			+ (volumes ? ", with the anonymous volumes it created" : "")
			+ (alive ? ". It is running, so it is killed first" : "")
			+ ". Named volumes are kept, and this cannot be undone.";
		return new DockerChangePlan(DockerCommands.Remove(name, volumes, alive), volumes ? $"Remove {name} and its volumes" : $"Remove {name}", message, "Remove");
	}
}
