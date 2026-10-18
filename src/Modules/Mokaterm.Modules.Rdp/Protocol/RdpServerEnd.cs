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
	private const byte TpktVersion = 3;
	private const byte X224DataLengthIndicator = 2;
	private const byte X224DataCode = 0xF0;

	// The first byte of an MCS domain PDU holds the CHOICE index in its top six bits (T.125, PER aligned).
	private const int McsDisconnectProviderUltimatum = 8;
	private const int McsSendDataIndication = 26;

	private const int ShareControlTypeMask = 0x0F;
	private const int ShareControlTypeData = 0x7;
	private const byte SetErrorInfoType = 0x2F;
	private const byte PacketCompressed = 0x20;

	// TS_SHARECONTROLHEADER (totalLength, pduType, pduSource), then the rest of TS_SHAREDATAHEADER (shareId, pad1,
	// streamId, uncompressedLength, pduType2, compressedType, compressedLength), then the four byte errorInfo.
	private const int PduType2Offset = 14;
	private const int CompressedTypeOffset = 15;
	private const int ErrorInfoOffset = 18;
	private const int SetErrorInfoLength = ErrorInfoOffset + 4;

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
		// A TPKT header and an X.224 Data TPDU, then the MCS PDU. Fast-path messages never carry either of these.
		if (message.Length < 9 || message[0] != TpktVersion || message[4] != X224DataLengthIndicator || message[5] != X224DataCode)
		{
			return null;
		}

		ReadOnlySpan<byte> mcs = message[7..];
		int choice = mcs[0] >> 2;
		if (choice == McsDisconnectProviderUltimatum)
		{
			// The reason is a three bit enumeration that straddles the first two bytes.
			int reason = ((mcs[0] & 0x03) << 1) | (mcs[1] >> 7);
			return FromUltimatum(reason);
		}

		if (choice != McsSendDataIndication || mcs.Length < 7)
		{
			return null;
		}

		// initiator (2 bytes), channelId (2, an offset from 0), dataPriority and segmentation (1), then a PER length.
		int channel = BinaryPrimitives.ReadUInt16BigEndian(mcs[3..]);
		if (channel != ioChannelId)
		{
			return null;
		}

		int at = 6;
		int length = mcs[at++];
		if ((length & 0x80) != 0)
		{
			if (mcs.Length <= at)
			{
				return null;
			}

			length = ((length & 0x7F) << 8) | mcs[at++];
		}

		if (length < SetErrorInfoLength || mcs.Length - at < length)
		{
			return null;
		}

		ReadOnlySpan<byte> share = mcs.Slice(at, length);
		int pduType = BinaryPrimitives.ReadUInt16LittleEndian(share[2..]);
		if ((pduType & ShareControlTypeMask) != ShareControlTypeData
			|| share[PduType2Offset] != SetErrorInfoType
			|| (share[CompressedTypeOffset] & PacketCompressed) != 0)
		{
			return null;
		}

		uint code = BinaryPrimitives.ReadUInt32LittleEndian(share[ErrorInfoOffset..]);
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
