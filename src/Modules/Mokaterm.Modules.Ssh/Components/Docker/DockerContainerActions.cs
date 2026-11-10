using System.Globalization;
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

	/// <summary>
	/// The change behind a bulk action on the picked containers: one command for every container it applies to (start
	/// those that are down, stop those that run, restart or remove them all), asked about once with every name in the
	/// question. Null when it applies to none of them. One container gets exactly what its own row's action does.
	/// </summary>
	public static DockerChangePlan? PlanMany(string action, IReadOnlyList<(string Name, bool Alive)> containers)
	{
		ArgumentNullException.ThrowIfNull(containers);
		List<(string Name, bool Alive)> targets = action switch
		{
			"start" => [.. containers.Where(container => !container.Alive)],
			"stop" => [.. containers.Where(container => container.Alive)],
			"restart" or "remove" => [.. containers],
			_ => [],
		};

		if (targets is [var only])
		{
			return Plan(action, only.Name, only.Alive);
		}

		if (targets.Count == 0)
		{
			return null;
		}

		List<string> names = [.. targets.Select(target => target.Name)];
		string list = DockerWording.NameList(names);
		string count = targets.Count.ToString(CultureInfo.CurrentCulture);
		return action switch
		{
			"start" => new DockerChangePlan(
				DockerCommands.Start(names),
				$"Start {count} containers",
				$"These {count} containers are started: {list}.",
				"Start",
				Destructive: false),
			"stop" => new DockerChangePlan(
				DockerCommands.Stop(names),
				$"Stop {count} containers",
				$"These {count} containers get SIGTERM and, any still running ten seconds later, SIGKILL: {list}.",
				"Stop"),
			"restart" => new DockerChangePlan(
				DockerCommands.Restart(names),
				$"Restart {count} containers",
				$"These {count} containers are stopped and started again: {list}.",
				"Restart",
				Destructive: false),
			_ => RemoveMany(targets, names, list, count),
		};
	}

	private static DockerChangePlan RemoveMany(List<(string Name, bool Alive)> targets, List<string> names, string list, string count)
	{
		List<string> alive = [.. targets.Where(target => target.Alive).Select(target => target.Name)];
		string running = alive switch
		{
			[] => "",
			[var one] => $" {one} is running, so it is killed first.",
			_ => $" {DockerWording.NameList(alive)} are running, so they are killed first.",
		};

		return new DockerChangePlan(
			DockerCommands.Remove(names, force: alive.Count > 0),
			$"Remove {count} containers",
			$"These {count} containers and their writable layers are deleted: {list}.{running} Named volumes are kept, and this cannot be undone.",
			"Remove");
	}

	private static DockerChangePlan Remove(string name, bool alive, bool volumes)
	{
		string message = $"{name} and its writable layer are deleted"
			+ (volumes ? ", with the anonymous volumes it created" : "")
			+ (alive ? ". It is running, so it is killed first" : "")
			+ ". Named volumes are kept, and this cannot be undone.";
		return new DockerChangePlan(DockerCommands.Remove(name, volumes, alive), volumes ? $"Remove {name} and its volumes" : $"Remove {name}", message, "Remove");
	}
}
