namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>A container's health check state, from <c>docker inspect</c>: the verdict and the checks that led to it.</summary>
internal sealed record DockerHealthCheck
{
	public DockerHealth Status { get; init; }

	/// <summary>How many checks in a row have failed.</summary>
	public int FailingStreak { get; init; }

	/// <summary>The last checks docker kept (five at most), newest last.</summary>
	public IReadOnlyList<DockerHealthProbe> Probes { get; init; } = [];
}
