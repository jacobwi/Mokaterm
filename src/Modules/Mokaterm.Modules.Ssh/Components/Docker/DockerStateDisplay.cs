using System.Globalization;
using Moka.Red.Core.Enums;
using Mokaterm.Modules.Ssh.Docker;

namespace Mokaterm.Modules.Ssh.Components.Docker;

/// <summary>One colour per container state, used by the list, the detail and the compose services alike.</summary>
internal static class DockerStateDisplay
{
	public static MokaColor Color(string state, DockerHealth health = DockerHealth.None) => state switch
	{
		"running" when health == DockerHealth.Unhealthy => MokaColor.Error,
		"running" when health == DockerHealth.Starting => MokaColor.Warning,
		"running" => MokaColor.Success,
		"paused" or "restarting" or "removing" => MokaColor.Warning,
		"dead" => MokaColor.Error,
		_ => MokaColor.Surface,
	};

	public static string Label(string state) => state.Length == 0 ? "UNKNOWN" : state.ToUpperInvariant();

	public static string HealthLabel(DockerHealth health) => health switch
	{
		DockerHealth.Healthy => "HEALTHY",
		DockerHealth.Unhealthy => "UNHEALTHY",
		DockerHealth.Starting => "STARTING",
		_ => "",
	};

	public static MokaColor HealthColor(DockerHealth health) => health switch
	{
		DockerHealth.Healthy => MokaColor.Success,
		DockerHealth.Unhealthy => MokaColor.Error,
		_ => MokaColor.Warning,
	};

	/// <summary>
	/// What a container's state tag says: its health while a check runs (a healthy container is a running one), else its
	/// state, with the code an exited one left.
	/// </summary>
	public static string TagText(DockerContainer container)
	{
		ArgumentNullException.ThrowIfNull(container);
		return container.State switch
		{
			"running" => container.Health switch
			{
				DockerHealth.Healthy => "Healthy",
				DockerHealth.Unhealthy => "Unhealthy",
				DockerHealth.Starting => "Starting",
				_ => "Running",
			},
			"exited" => ExitCode(container.Status) is { } code ? string.Create(CultureInfo.InvariantCulture, $"Exited ({code})") : "Exited",
			var state => StateWord(state),
		};
	}

	/// <summary>Docker's state as a tag writes it: <c>Running</c>, <c>Exited</c>.</summary>
	public static string StateWord(string state)
	{
		ArgumentNullException.ThrowIfNull(state);
		return state.Length == 0 ? "Unknown" : string.Concat(state[..1].ToUpperInvariant(), state.AsSpan(1));
	}

	/// <summary>
	/// What docker's status says besides the tag: <c>Up 2 days</c>, <c>3 hours ago</c> for one that exited or is waiting
	/// to restart, nothing for one only created.
	/// </summary>
	public static string Since(DockerContainer container)
	{
		ArgumentNullException.ThrowIfNull(container);
		string text = container.StatusText;
		if (text.StartsWith("Up ", StringComparison.Ordinal))
		{
			int paused = text.IndexOf(" (Paused)", StringComparison.Ordinal);
			return paused > 0 ? text[..paused] : text;
		}

		int close = text.IndexOf(") ", StringComparison.Ordinal);
		return close > 0 && (text.StartsWith("Exited (", StringComparison.Ordinal) || text.StartsWith("Restarting (", StringComparison.Ordinal))
			? text[(close + 2)..]
			: "";
	}

	/// <summary>The order the status column sorts in: what needs a look first, then what runs, then what does not.</summary>
	public static int Rank(DockerContainer container)
	{
		ArgumentNullException.ThrowIfNull(container);
		return container.State switch
		{
			"running" when container.Health == DockerHealth.Unhealthy => 0,
			"dead" => 1,
			"restarting" => 2,
			"running" when container.Health == DockerHealth.Starting => 3,
			"paused" or "removing" => 4,
			"running" => 5,
			"created" => 6,
			_ => 7,
		};
	}

	/// <summary>The code in <c>Exited (137) 3 hours ago</c>.</summary>
	private static int? ExitCode(string status)
	{
		int open = status.IndexOf('(', StringComparison.Ordinal);
		int close = status.IndexOf(')', StringComparison.Ordinal);
		return open >= 0 && close > open && int.TryParse(status.AsSpan(open + 1, close - open - 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int code)
			? code
			: null;
	}
}
