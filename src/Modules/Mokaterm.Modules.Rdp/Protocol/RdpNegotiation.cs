using System.Buffers.Binary;
using System.Globalization;

namespace Mokaterm.Modules.Rdp.Protocol;

/// <summary>
/// The server's answer to the X.224 Connection Request (MS-RDPBCGR 2.2.1.2): the security protocol it picked
/// (RDP_NEG_RSP), or why it refused the ones asked for (RDP_NEG_FAILURE).
/// </summary>
/// <remarks>
/// IronRDP checks the answer too, but only that the pick shares a bit with the request, and it reports a refusal as a
/// sentence of its own. This is read first so that a pick outside the request stops the attempt before the TLS
/// handshake, and so that a refusal is worded by its code.
/// </remarks>
/// <param name="Selected">The protocol the server picked; <see cref="RdpSecurityProtocols.Rdp"/> after a refusal.</param>
/// <param name="FailureCode">RDP_NEG_FAILURE's failureCode, or null when the server picked a protocol.</param>
internal sealed record RdpNegotiation(RdpSecurityProtocols Selected, uint? FailureCode)
{
	/// <summary>SSL_REQUIRED_BY_SERVER: TLS is required, and a request for CredSSP alone means the server only does TLS.</summary>
	public const uint SslRequiredByServer = 1;

	/// <summary>SSL_NOT_ALLOWED_BY_SERVER: the server only does Standard RDP Security.</summary>
	public const uint SslNotAllowedByServer = 2;

	/// <summary>SSL_CERT_NOT_ON_SERVER: the server has no certificate to start TLS with.</summary>
	public const uint SslCertNotOnServer = 3;

	/// <summary>INCONSISTENT_FLAGS: the request does not fit the security already in effect.</summary>
	public const uint InconsistentFlags = 4;

	/// <summary>HYBRID_REQUIRED_BY_SERVER: the server requires network level authentication.</summary>
	public const uint HybridRequiredByServer = 5;

	/// <summary>SSL_WITH_USER_AUTH_REQUIRED_BY_SERVER: the server wants a client certificate.</summary>
	public const uint SslWithUserAuthRequiredByServer = 6;

	private const byte TpktVersion = 3;
	private const byte ConnectionConfirmCode = 0xD0;

	// The X.224 Connection Confirm's fixed part after the length indicator: code, destination and source references, class.
	private const int ConfirmFixedLength = 6;
	private const int NegotiationOffset = 11;
	private const int NegotiationLength = 8;
	private const byte TypeResponse = 0x02;
	private const byte TypeFailure = 0x03;

	/// <summary>
	/// What IronRDP asks for, which is what <see cref="RdpConfigFactory"/> configures: TLS always, and CredSSP in both
	/// its forms when network level authentication is on.
	/// </summary>
	public static RdpSecurityProtocols Requested(bool credssp) =>
		credssp ? RdpSecurityProtocols.Ssl | RdpSecurityProtocols.Hybrid | RdpSecurityProtocols.HybridEx : RdpSecurityProtocols.Ssl;

	/// <summary>
	/// The answer in <paramref name="message"/>, or null when it is not an X.224 Connection Confirm, which IronRDP then
	/// reports as something that is not RDP. Never throws on short or malformed input.
	/// </summary>
	public static RdpNegotiation? Read(ReadOnlySpan<byte> message)
	{
		if (message.Length < 4 + 1 + ConfirmFixedLength || message[0] != TpktVersion || (message[5] & 0xF0) != ConnectionConfirmCode)
		{
			return null;
		}

		// A server from before RDP 5.2 sends no negotiation at all, which means Standard RDP Security.
		int variablePart = message[4] - ConfirmFixedLength;
		if (variablePart <= 0)
		{
			return new RdpNegotiation(RdpSecurityProtocols.Rdp, null);
		}

		if (variablePart < NegotiationLength || message.Length < NegotiationOffset + NegotiationLength)
		{
			return null;
		}

		ReadOnlySpan<byte> negotiation = message.Slice(NegotiationOffset, NegotiationLength);
		uint value = BinaryPrimitives.ReadUInt32LittleEndian(negotiation[4..]);
		return negotiation[0] switch
		{
			TypeResponse => new RdpNegotiation((RdpSecurityProtocols)value, null),
			TypeFailure => new RdpNegotiation(RdpSecurityProtocols.Rdp, value),
			_ => null,
		};
	}

	/// <summary>
	/// True when the server picked exactly one of the protocols <paramref name="requested"/> named. Standard RDP
	/// Security is never one of them, and a pick of several bits at once is no answer RDP defines.
	/// </summary>
	public bool Picked(RdpSecurityProtocols requested) =>
		FailureCode is null
		&& Selected != RdpSecurityProtocols.Rdp
		&& (Selected & (Selected - 1)) == 0
		&& (Selected & requested) == Selected;

	/// <summary>The protocol's name as MS-RDPBCGR writes it, for the probe and the log. Never holds a host or a user name.</summary>
	public static string Name(RdpSecurityProtocols protocol) => protocol switch
	{
		RdpSecurityProtocols.Rdp => "PROTOCOL_RDP",
		RdpSecurityProtocols.Ssl => "PROTOCOL_SSL",
		RdpSecurityProtocols.Hybrid => "PROTOCOL_HYBRID",
		RdpSecurityProtocols.RdsTls => "PROTOCOL_RDSTLS",
		RdpSecurityProtocols.HybridEx => "PROTOCOL_HYBRID_EX",
		RdpSecurityProtocols.RdsAad => "PROTOCOL_RDSAAD",
		_ => string.Create(CultureInfo.InvariantCulture, $"protocols 0x{(uint)protocol:X8}"),
	};

	/// <summary>RDP_NEG_FAILURE's code as MS-RDPBCGR names it.</summary>
	public static string FailureName(uint code) => code switch
	{
		SslRequiredByServer => "SSL_REQUIRED_BY_SERVER",
		SslNotAllowedByServer => "SSL_NOT_ALLOWED_BY_SERVER",
		SslCertNotOnServer => "SSL_CERT_NOT_ON_SERVER",
		InconsistentFlags => "INCONSISTENT_FLAGS",
		HybridRequiredByServer => "HYBRID_REQUIRED_BY_SERVER",
		SslWithUserAuthRequiredByServer => "SSL_WITH_USER_AUTH_REQUIRED_BY_SERVER",
		_ => string.Create(CultureInfo.InvariantCulture, $"0x{code:X8}"),
	};
}
