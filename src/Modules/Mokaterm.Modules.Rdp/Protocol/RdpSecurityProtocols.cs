namespace Mokaterm.Modules.Rdp.Protocol;

/// <summary>
/// The security protocols of an RDP negotiation (MS-RDPBCGR 2.2.1.1.1 and 2.2.1.2.1), as the client's request lists them
/// and the server's answer picks one. The values are the wire values.
/// </summary>
[Flags]
internal enum RdpSecurityProtocols : uint
{
	/// <summary>PROTOCOL_RDP, Standard RDP Security: RC4 with keys the server's own certificate format protects. Never used here.</summary>
	Rdp = 0,

	/// <summary>PROTOCOL_SSL: TLS, with the login checked by Windows' own sign-in.</summary>
	Ssl = 0x01,

	/// <summary>PROTOCOL_HYBRID: TLS and then CredSSP, which checks the login before the session starts.</summary>
	Hybrid = 0x02,

	/// <summary>PROTOCOL_RDSTLS, which carries a redirection's credentials. Never asked for here.</summary>
	RdsTls = 0x04,

	/// <summary>PROTOCOL_HYBRID_EX: CredSSP followed by the Early User Authorization Result PDU.</summary>
	HybridEx = 0x08,

	/// <summary>PROTOCOL_RDSAAD, Microsoft Entra ID web sign-in. IronRDP defines the flag and implements none of it.</summary>
	RdsAad = 0x10,
}
