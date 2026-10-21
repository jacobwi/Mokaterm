using System.Buffers.Binary;
using System.Globalization;

namespace Mokaterm.Modules.Rdp.Protocol;

/// <summary>
/// The server ending a session on purpose. Windows says why in a Set Error Info PDU (MS-RDPBCGR 2.2.5.1.1, sent because
/// IronRDP's connector sets RNS_UD_CS_SUPPORT_ERRINFO_PDU) and then sends an MCS Disconnect Provider Ultimatum
/// (2.2.2.3) before it closes the connection: that is the server-side disconnection sequence of 1.3.1.4.2 and 1.3.1.4.3,
/// for a user who signs out or disconnects, an administrator who does it for them, and another user taking the session.
/// </summary>
/// <remarks>
/// These messages are read here, before IronRDP sees them, because IronRDP 2025.12.4 cannot tell us any of it: its
/// <c>Terminate</c> output carries a <c>GracefulDisconnectReason</c> the C# bindings give no accessor for, an ultimatum
/// fails <c>decode_send_data_indication</c> as an error, an error code it does not know fails the PDU's decode, and
/// during a reactivation every one of them is an "unexpected PDU" error. Each of those would read as a dropped
/// connection.
/// </remarks>
/// <param name="Reason">What the user is told.</param>
/// <param name="Reconnects">True when this counts as a drop, so the session faults and the automatic reconnect applies.</param>
/// <param name="Code">Which message and code it was, for the log. Never holds a host or user name.</param>
internal sealed record RdpServerEnd(string Reason, bool Reconnects, string Code)
{
	private const byte SetErrorInfoType = 0x2F;

	/// <summary>IronRDP ended the session itself, which leaves no reason this side can read.</summary>
	public static RdpServerEnd Terminated { get; } = new("The server ended the remote session.", Reconnects: false, "IronRDP terminate");

	/// <summary>
	/// The end <paramref name="message"/> announces, or null when it is anything else. Only a Set Error Info PDU on the
	/// session's own channel counts, since the bytes of a virtual channel's data could look the same; a Set Error Info
	/// of zero is the server clearing the code, which IronRDP handles. Never throws on short or malformed input.
	/// </summary>
	/// <param name="message">One whole message from the server, as the frame reader returns it.</param>
	/// <param name="ioChannelId">The MCS I/O channel the server sends the session's own PDUs on.</param>
	public static RdpServerEnd? Read(ReadOnlySpan<byte> message, int ioChannelId)
	{
		if (RdpIoChannel.Ultimatum(message) is int reason)
		{
			return FromUltimatum(reason);
		}

		if (!RdpIoChannel.TryReadShareData(message, ioChannelId, out byte pduType2, out ReadOnlySpan<byte> data)
			|| pduType2 != SetErrorInfoType
			|| data.Length < 4)
		{
			return null;
		}

		uint code = BinaryPrimitives.ReadUInt32LittleEndian(data);
		return code == RdpErrorInfo.None ? null : FromErrorInfo(code);
	}

	public static RdpServerEnd FromErrorInfo(uint code) =>
		new(RdpErrorInfo.Describe(code), RdpErrorInfo.Reconnects(code), "Set Error Info " + RdpErrorInfo.Hex(code));

	/// <summary>
	/// An ultimatum with no Set Error Info before it, which FreeRDP's <c>rdp_recv_tpkt_pdu</c> says older servers such
	/// as Windows Server 2008 R2 send when the user signs out. FreeRDP reads rn-user-requested as a sign out and every
	/// other reason as a disconnect, and never reconnects after either.
	/// </summary>
	/// <param name="reason">T.125's Reason: 0 domain disconnected, 1 provider initiated, 2 token purged, 3 user requested, 4 channel purged.</param>
	public static RdpServerEnd FromUltimatum(int reason) => new(
		reason == 3
			? "The remote session was signed out or disconnected on the server."
			: "The server disconnected the remote session.",
		Reconnects: false,
		string.Create(CultureInfo.InvariantCulture, $"Disconnect Provider Ultimatum, reason {reason}"));
}
