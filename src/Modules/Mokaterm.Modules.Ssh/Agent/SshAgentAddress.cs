using System.Globalization;

namespace Mokaterm.Modules.Ssh.Agent;

internal enum SshAgentTransport
{
	/// <summary>A Windows named pipe, such as <c>openssh-ssh-agent</c>.</summary>
	NamedPipe,

	/// <summary>A Unix domain socket, which is what <c>SSH_AUTH_SOCK</c> names outside Windows.</summary>
	UnixSocket,
}

/// <summary>Where an agent listens. <see cref="Name"/> is the pipe name without its prefix, or the socket path.</summary>
internal sealed record SshAgentAddress(SshAgentTransport Transport, string Name)
{
	public string Display => Transport == SshAgentTransport.NamedPipe
		? string.Create(CultureInfo.InvariantCulture, $@"\\.\pipe\{Name}")
		: Name;
}
