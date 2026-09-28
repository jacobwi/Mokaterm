namespace Mokaterm.Modules.Ftp;

/// <summary>How listings and file contents reach the client.</summary>
public enum FtpDataConnectionMode
{
	/// <summary>
	/// PASV: the client connects to a port the server opens, always on the address the session is connected to,
	/// whatever address the reply names. Works through most firewalls.
	/// </summary>
	Passive,

	/// <summary>EPSV: like passive, but the reply carries no address, which suits IPv6 and servers behind NAT.</summary>
	ExtendedPassive,

	/// <summary>PORT or EPRT: the server connects back to the machine running Mokaterm, which needs an open port there.</summary>
	Active,
}
