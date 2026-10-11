namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>What looking for Docker on the server found, kept per session until the user looks again.</summary>
internal sealed record DockerEngineStatus
{
	public required DockerAvailability Availability { get; init; }

	/// <summary>Why it is not ready, written for the user; null when it is.</summary>
	public string? Message { get; init; }

	public DockerVersionInfo? Version { get; init; }

	public DockerEngineInfo? Info { get; init; }

	/// <summary>Which composes the server has; the session's preference picks the one commands use.</summary>
	public ComposeDetection Compose { get; init; } = ComposeDetection.None;

	/// <summary>True when the commands that found this ran through sudo.</summary>
	public bool IsElevated { get; init; }

	public bool IsReady => Availability == DockerAvailability.Ready;
}
