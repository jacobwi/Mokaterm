using Mokaterm.Modules.Ssh.Docker;

namespace Mokaterm.Modules.Ssh.Components.Docker;

/// <summary>
/// What each compose action runs, on the whole project or on one service, and what it asks first. The title names the
/// services it touches, which is what the run line shows while it works.
/// </summary>
internal static class ComposeActions
{
	/// <summary>The calls behind <paramref name="action"/>, one after the other, or null for an action that runs nothing.</summary>
	/// <param name="services">Every service of the project, for naming what a project-wide action touches.</param>
	/// <param name="service">One service, or null for the whole project.</param>
	/// <param name="build">For <c>up</c>: build images first.</param>
	/// <param name="pullFirst">For <c>up</c>: pull images first, as its own command, which the Python compose needs.</param>
	public static ComposeActionPlan? Plan(
		string action,
		ComposeTarget target,
		IReadOnlyList<string> services,
		string? service = null,
		bool build = false,
		bool pullFirst = false)
	{
		ArgumentNullException.ThrowIfNull(target);
		ArgumentNullException.ThrowIfNull(services);
		string project = target.Project ?? DockerHostPaths.NameOf(target.WorkingDirectory ?? "project");
		string touched = service ?? (services.Count == 0 ? project : $"{project} ({string.Join(", ", services)})");
		return action switch
		{
			"restart" => new([ComposeCommands.Restart(target, service)], $"Restart {touched}"),
			"start" => new([ComposeCommands.Start(target, service)], $"Start {touched}"),
			"stop" => new(
				[ComposeCommands.Stop(target, service)],
				$"Stop {touched}",
				$"Every container of {service ?? project} gets SIGTERM and, ten seconds later, SIGKILL. They stay, and Start brings them back.",
				"Stop"),
			"pull" => new([ComposeCommands.Pull(target, service)], $"Pull images for {touched}"),
			"update" => new(
				[ComposeCommands.Pull(target, service), ComposeCommands.Up(target, service: service)],
				$"Update {touched}",
				$"Compose pulls the newest images for {service ?? project}, then makes again every container whose image changed. What was written inside those containers, outside volumes, is lost.",
				"Update"),
			"up" => new(
				pullFirst ? [ComposeCommands.Pull(target), ComposeCommands.Up(target, build)] : [ComposeCommands.Up(target, build)],
				(build ? "Build and start " : "Start ") + touched),
			"recreate" => new(
				[service is null ? ComposeCommands.Up(target, forceRecreate: true) : ComposeCommands.Recreate(target, service)],
				$"Recreate {touched}",
				service is null
					? $"Every container of {project} is removed and made again from its image, even those whose configuration did not change. What was written inside them, outside volumes, is lost."
					: $"{service}'s containers are removed and made again from the image, and nothing it depends on is touched. What was written inside them, outside volumes, is lost.",
				"Recreate"),
			"down" or "down-volumes" => Down(target, project, action == "down-volumes"),
			_ => null,
		};
	}

	private static ComposeActionPlan Down(ComposeTarget target, string project, bool volumes) => new(
		[ComposeCommands.Down(target, volumes)],
		volumes ? $"Take down {project} and its volumes" : $"Take down {project}",
		$"{project}'s containers are stopped and removed, with the networks compose made for it"
			+ (volumes
				? ", and its volumes: the named ones its files declare and the anonymous ones. Whatever they store is deleted."
				: ". Its volumes are kept."),
		"Take down");
}
