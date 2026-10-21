namespace Mokaterm.Modules.Rdp.Protocol;

/// <summary>How a connect attempt signs in, decided before anything is dialed by <see cref="RdpConnector.RouteFor"/>.</summary>
internal enum RdpSignInRoute
{
	/// <summary>
	/// Network level authentication: CredSSP checks the login before the server builds a session. IronRDP also puts the
	/// name and password in the Client Info PDU afterwards, flagged INFO_AUTOLOGON, so a server that picks TLS alone
	/// signs in the way <see cref="Automatic"/> does.
	/// </summary>
	NetworkLevelAuthentication,

	/// <summary>
	/// TLS alone, with the name and password in the Client Info PDU (TS_INFO_PACKET) flagged INFO_AUTOLOGON, which is how
	/// mstsc signs in to a PC that has network level authentication off. It is the only way in from here for a Microsoft
	/// Entra ID account, whose CredSSP needs PKU2U or Entra's web sign-in.
	/// </summary>
	Automatic,

	/// <summary>TLS alone and no password: the server shows its own sign-in screen, with the user name filled in when there is one.</summary>
	SignInScreen,
}
