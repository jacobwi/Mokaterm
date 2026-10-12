namespace Mokaterm.Modules.Ssh.Admin.Reboot;

/// <summary>Where reconnecting after a reboot has got to.</summary>
internal enum RebootWatchState
{
	/// <summary>The reboot was sent and the session is still up.</summary>
	WaitingForDown,

	/// <summary>The session dropped; the server is asked now and then whether it answers SSH again.</summary>
	WaitingForServer,

	/// <summary>The server answered and the session is connecting.</summary>
	Reconnecting,

	/// <summary>The session is connected again.</summary>
	Back,

	/// <summary>It went on too long, or the login needs the user; the reason is in <see cref="RebootWatch.Problem"/>.</summary>
	GaveUp,

	/// <summary>The user stopped waiting, or the tab closed.</summary>
	Stopped,
}
