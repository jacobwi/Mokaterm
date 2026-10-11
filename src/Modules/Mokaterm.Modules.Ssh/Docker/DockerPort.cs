namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>One published or exposed port of a container.</summary>
/// <param name="HostAddress">The address it listens on (<c>0.0.0.0</c>, <c>::</c>, <c>127.0.0.1</c>), or empty when it is only exposed.</param>
/// <param name="HostPort">The port on the docker host, or null when it is only exposed.</param>
/// <param name="ContainerPort">The port inside, or a range such as <c>8000-8010</c>.</param>
/// <param name="Protocol"><c>tcp</c>, <c>udp</c> or <c>sctp</c>.</param>
internal sealed record DockerPort(string HostAddress, string? HostPort, string ContainerPort, string Protocol)
{
	public bool IsPublished => HostPort is not null;

	/// <summary><c>8080→80/tcp</c>, <c>127.0.0.1:5432→5432/tcp</c>, or <c>80/tcp</c> when it is only exposed.</summary>
	public string Display
	{
		get
		{
			string inside = $"{ContainerPort}/{Protocol}";
			if (HostPort is null)
			{
				return inside;
			}

			bool everywhere = HostAddress is "" or "0.0.0.0" or "::" or "[::]";
			return everywhere ? $"{HostPort}→{inside}" : $"{HostAddress}:{HostPort}→{inside}";
		}
	}
}
