namespace Mokaterm.Abstractions.Protocols;

/// <summary>How a connection reaches its server when it cannot dial the address itself.</summary>
public enum ProxyKind
{
	/// <summary>Dial the server directly.</summary>
	None,

	/// <summary>An HTTP proxy asked for a tunnel with CONNECT. Its login, when it needs one, is HTTP basic.</summary>
	Http,

	/// <summary>SOCKS4, which takes an address the client resolved and the proxy user as its ident string.</summary>
	Socks4,

	/// <summary>SOCKS5, which can resolve the host name itself and can ask for a user name and password.</summary>
	Socks5,
}
