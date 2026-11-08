using System.Globalization;
using Moka.Red.Core.Enums;
using Moka.Red.Primitives.DataList;
using Mokaterm.Modules.Ssh.Docker;
using Mokaterm.UI.Common.Formatting;

namespace Mokaterm.Modules.Ssh.Components.Docker;

// The environment is left out on purpose: the Env tab shows it, one value at a time.
internal static class DockerInspectSummary
{
	public static IReadOnlyList<InspectTile> Tiles(DockerContainerDetails details, DateTimeOffset now)
	{
		ArgumentNullException.ThrowIfNull(details);
		List<InspectTile> tiles = [new("State", Capitalized(details.Status), StateHelp(details), StateColor(details))];

		if (details.Status == "running" && details.StartedAt is { } started)
		{
			tiles.Add(new("Up", DisplayFormat.Span(now - started), "since " + DisplayFormat.Timestamp(started), null));
		}
		else if (details.FinishedAt is { } finished)
		{
			string? ran = details.StartedAt is { } begun && finished > begun ? "after running " + DisplayFormat.Span(finished - begun) : null;
			tiles.Add(new("Stopped", DisplayFormat.Relative(finished, now), ran, null));
		}

		tiles.Add(new(
			"Restarts",
			details.RestartCount.ToString("N0", CultureInfo.CurrentCulture),
			"policy " + Or(details.RestartPolicy, "no"),
			details.RestartCount > 0 ? MokaColor.Warning : null));

		if (details.Health is { Status: not DockerHealth.None } health)
		{
			string? streak = health.FailingStreak > 0
				? string.Create(CultureInfo.CurrentCulture, $"{health.FailingStreak} failing in a row")
				: health.Probes.Count > 0 && health.Probes[^1].Start is { } checkedAt ? "checked " + DisplayFormat.Relative(checkedAt, now) : null;
			tiles.Add(new("Health", Capitalized(DockerStateDisplay.HealthLabel(health.Status).ToLowerInvariant()), streak, DockerStateDisplay.HealthColor(health.Status)));
		}

		return tiles;
	}

	public static List<MokaDataListItem> Runtime(DockerContainerDetails details)
	{
		ArgumentNullException.ThrowIfNull(details);
		List<MokaDataListItem> facts =
		[
			new("Status", details.Status
				+ (details.ExitCode is { } exit && details.Status != "running" ? string.Create(CultureInfo.InvariantCulture, $" (exit {exit})") : "")
				+ (details.OomKilled ? ", killed out of memory" : "")),
		];
		if (details.Error.Length > 0)
		{
			facts.Add(new("Error", details.Error));
		}

		facts.Add(new("Created", When(details.Created)));
		facts.Add(new("Started", When(details.StartedAt)));
		if (details.Status != "running")
		{
			facts.Add(new("Finished", When(details.FinishedAt)));
		}

		facts.Add(new("Restart", string.Create(CultureInfo.CurrentCulture, $"{Or(details.RestartPolicy, "no")}, restarted {details.RestartCount} {(details.RestartCount == 1 ? "time" : "times")}")));
		if (details.Health is { } health)
		{
			facts.Add(new("Health", HealthText(health)));
		}

		if (details.Pid is { } pid)
		{
			facts.Add(new("PID", pid.ToString(CultureInfo.InvariantCulture)));
		}

		facts.Add(new("Hostname", details.Hostname));
		facts.Add(new("Network mode", Or(details.NetworkMode, "default")));
		if (details.Privileged)
		{
			facts.Add(new("Privileged", "yes, with every capability and every device"));
		}

		return facts;
	}

	public static List<MokaDataListItem> Command(DockerContainerDetails details)
	{
		ArgumentNullException.ThrowIfNull(details);
		List<MokaDataListItem> facts = [new("Image", details.Image)];
		if (ShortImageId(details.ImageId) is { Length: > 0 } imageId)
		{
			facts.Add(new("Image id", imageId));
		}

		if (details.Entrypoint.Count > 0)
		{
			facts.Add(new("Entrypoint", string.Join(' ', details.Entrypoint)));
		}

		if (details.Command.Count > 0)
		{
			facts.Add(new("Command", string.Join(' ', details.Command)));
		}

		facts.Add(new("Working dir", Or(details.WorkingDirectory, "/")));
		facts.Add(new("User", Or(details.User, "the image's own (root unless it says otherwise)")));
		return facts;
	}

	public static List<MokaDataListItem> Networks(DockerContainerDetails details)
	{
		ArgumentNullException.ThrowIfNull(details);
		return [.. details.Networks.Select(network => new MokaDataListItem(
			network.Network,
			string.Join(" · ", new[] { network.IpAddress, network.Ipv6Address, string.Join(", ", network.Aliases) }.Where(part => part.Length > 0))))];
	}

	public static List<MokaDataListItem> Ports(DockerContainerDetails details)
	{
		ArgumentNullException.ThrowIfNull(details);
		return [.. details.Ports.Select(port => new MokaDataListItem(
			$"{port.ContainerPort}/{port.Protocol}",
			port.HostPort is { } hostPort ? $"{Or(port.HostAddress, "0.0.0.0")}:{hostPort}" : "not published"))];
	}

	public static List<MokaDataListItem> Mounts(DockerContainerDetails details)
	{
		ArgumentNullException.ThrowIfNull(details);
		return [.. details.Mounts.Select(mount => new MokaDataListItem(
			mount.Destination,
			string.Join(" · ", new[] { mount.Type, mount.Name.Length > 0 ? mount.Name : mount.Source, mount.ReadWrite ? "read-write" : "read-only" }.Where(part => part.Length > 0))))];
	}

	public static List<MokaDataListItem> Labels(DockerContainerDetails details)
	{
		ArgumentNullException.ThrowIfNull(details);
		return [.. details.Labels.OrderBy(label => label.Key, StringComparer.Ordinal).Select(label => new MokaDataListItem(label.Key, label.Value))];
	}

	public static string When(DateTimeOffset? value) =>
		value?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "never";

	private static string? StateHelp(DockerContainerDetails details)
	{
		List<string> parts = [];
		if (details.Status != "running" && details.ExitCode is { } exit)
		{
			parts.Add(string.Create(CultureInfo.InvariantCulture, $"exit {exit}"));
		}

		if (details.OomKilled)
		{
			parts.Add("killed out of memory");
		}

		if (details.Status == "running" && details.Pid is { } pid)
		{
			parts.Add(string.Create(CultureInfo.InvariantCulture, $"PID {pid}"));
		}

		return parts.Count > 0 ? string.Join(", ", parts) : null;
	}

	private static MokaColor? StateColor(DockerContainerDetails details) => details.Status switch
	{
		_ when details.OomKilled => MokaColor.Error,
		"running" when details.Health?.Status == DockerHealth.Unhealthy => MokaColor.Warning,
		"running" => MokaColor.Success,
		"paused" or "restarting" => MokaColor.Warning,
		"dead" => MokaColor.Error,
		"exited" when details.ExitCode is not (null or 0) => MokaColor.Error,
		_ => null,
	};

	private static string HealthText(DockerHealthCheck health) => health.FailingStreak > 0
		? string.Create(CultureInfo.CurrentCulture, $"{DockerStateDisplay.HealthLabel(health.Status).ToLowerInvariant()}, {health.FailingStreak} failing in a row")
		: DockerStateDisplay.HealthLabel(health.Status).ToLowerInvariant();

	private static string ShortImageId(string imageId)
	{
		string hex = imageId.StartsWith("sha256:", StringComparison.Ordinal) ? imageId["sha256:".Length..] : imageId;
		return hex.Length > 12 ? hex[..12] : hex;
	}

	private static string Capitalized(string word) =>
		word.Length == 0 ? "Unknown" : char.ToUpperInvariant(word[0]) + word[1..];

	private static string Or(string value, string fallback) => value.Length > 0 ? value : fallback;
}
