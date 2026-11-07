namespace Mokaterm.Abstractions.Protocols;

/// <summary>What a <see cref="ConnectStep"/> does, which is how the shell picks the icon it draws for the step.</summary>
public enum ConnectStepKind
{
	/// <summary>Anything the other kinds do not describe.</summary>
	Other,

	/// <summary>Reaching the server through something else first, such as a jump host.</summary>
	Route,

	/// <summary>Resolving the name and opening the connection.</summary>
	Dial,

	/// <summary>Agreeing on the protocol with the server: versions, security layers, options.</summary>
	Negotiate,

	/// <summary>Setting up encryption, such as a TLS handshake.</summary>
	Encryption,

	/// <summary>Checking that the server is the machine it claims to be: a host key or a certificate.</summary>
	HostIdentity,

	/// <summary>Signing in.</summary>
	Login,

	/// <summary>Opening what the session is for: a shell, a desktop, a file channel. The shell draws the protocol's own icon.</summary>
	Session,
}
