namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>One thing the health watch noticed, with the sentence the notice shows.</summary>
internal sealed record DockerHealthAlert(DockerHealthEvent Event, string Container, string Message)
{
	/// <summary>An exit with an error code is an error; the rest are warnings.</summary>
	public bool IsError { get; init; }
}
