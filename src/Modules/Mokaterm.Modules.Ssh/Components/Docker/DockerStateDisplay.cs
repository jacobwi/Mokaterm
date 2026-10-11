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

	/// <summary>True for the states that are on their way somewhere, which pulse.</summary>
	public static bool IsMoving(string state, DockerHealth health = DockerHealth.None) =>
		state is "restarting" or "removing" || (state == "running" && health == DockerHealth.Starting);

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
}
