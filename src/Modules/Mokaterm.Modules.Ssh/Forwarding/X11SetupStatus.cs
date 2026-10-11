namespace Mokaterm.Modules.Ssh.Forwarding;

internal enum X11SetupStatus
{
	/// <summary>The setup is not all there yet.</summary>
	NeedMore,

	/// <summary>Not an X11 connection setup: the first byte names no byte order.</summary>
	Invalid,

	/// <summary>The whole setup is in the buffer.</summary>
	Complete,
}
