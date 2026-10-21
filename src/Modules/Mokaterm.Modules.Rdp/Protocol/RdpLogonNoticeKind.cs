namespace Mokaterm.Modules.Rdp.Protocol;

/// <summary>What a Save Session Info PDU says about signing in, in the terms the probe and the log need.</summary>
internal enum RdpLogonNoticeKind
{
	/// <summary>Windows signed a user in and the session is theirs (INFOTYPE_LOGON, INFOTYPE_LOGON_LONG or INFOTYPE_LOGON_PLAINNOTIFY).</summary>
	SignedIn,

	/// <summary>Windows is still signing in (LOGON_MSG_SESSION_CONTINUE), which says nothing about how it ends.</summary>
	Continuing,

	/// <summary>Windows could not sign in with what it was given and shows its sign-in screen (a logon error with an NTSTATUS).</summary>
	SignInFailed,

	/// <summary>Winlogon shows a dialog in the session: another user signed in, a busy session, no permission and the like.</summary>
	Dialog,
}
