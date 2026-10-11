namespace Mokaterm.Modules.Ssh.Forwarding;

internal enum SshForwardingState
{
	/// <summary>The connection does not ask for it.</summary>
	Off,

	/// <summary>Asked for when the shell opened; the server's programs can use it if the server allows it.</summary>
	Requested,

	/// <summary>Asked for by the connection but not set up, for the reason the status gives.</summary>
	Unavailable,
}
