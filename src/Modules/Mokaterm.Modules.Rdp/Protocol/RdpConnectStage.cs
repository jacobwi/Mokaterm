namespace Mokaterm.Modules.Rdp.Protocol;

/// <summary>
/// The steps of an RDP connect, in the order they run. A failure or a timeout names the one it reached, because "it
/// timed out" alone cannot tell a firewall from a domain controller nobody can reach.
/// </summary>
internal enum RdpConnectStage
{
	/// <summary>Resolving the name and opening the TCP connection.</summary>
	Dialing,

	/// <summary>The X.224 request that asks for TLS and CredSSP, and the server's answer.</summary>
	Negotiating,

	/// <summary>The TLS handshake.</summary>
	Tls,

	/// <summary>The server's certificate goes to the user's known hosts, and maybe to the user.</summary>
	Certificate,

	/// <summary>CredSSP: NTLM or Kerberos over TLS, before the server builds a session.</summary>
	Login,

	/// <summary>A Kerberos request CredSSP asked to have sent to the domain's Kerberos server.</summary>
	Kerberos,

	/// <summary>The connection sequence: settings, channels, licensing and capabilities.</summary>
	Session,
}
