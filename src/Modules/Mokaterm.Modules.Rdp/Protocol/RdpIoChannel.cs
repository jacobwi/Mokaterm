using System.Buffers.Binary;

namespace Mokaterm.Modules.Rdp.Protocol;

/// <summary>
/// Reads the slow-path messages a server sends on the session's own MCS channel far enough to know what they are, for
/// the few that are looked at before IronRDP: a session's end (<see cref="RdpServerEnd"/>) and a sign-in notice
/// (<see cref="RdpLogonNotice"/>). Never throws on short or malformed input.
/// </summary>
internal static class RdpIoChannel
{
	private const byte TpktVersion = 3;
	private const byte X224DataLengthIndicator = 2;
	private const byte X224DataCode = 0xF0;

	// The first byte of an MCS domain PDU holds the CHOICE index in its top six bits (T.125, PER aligned).
	private const int McsDisconnectProviderUltimatum = 8;
	private const int McsSendDataIndication = 26;

	private const int ShareControlTypeMask = 0x0F;
	private const int ShareControlTypeData = 0x7;
	private const byte PacketCompressed = 0x20;

	// TS_SHARECONTROLHEADER (totalLength, pduType, pduSource), then the rest of TS_SHAREDATAHEADER (shareId, pad1,
	// streamId, uncompressedLength, pduType2, compressedType, compressedLength); the PDU's own data follows.
	private const int PduType2Offset = 14;
	private const int CompressedTypeOffset = 15;
	private const int DataOffset = 18;

	/// <summary>
	/// The reason of an MCS Disconnect Provider Ultimatum (MS-RDPBCGR 2.2.2.3), or null when <paramref name="message"/>
	/// is anything else.
	/// </summary>
	public static int? Ultimatum(ReadOnlySpan<byte> message)
	{
		if (!IsX224Data(message))
		{
			return null;
		}

		ReadOnlySpan<byte> mcs = message[7..];
		if (mcs[0] >> 2 != McsDisconnectProviderUltimatum)
		{
			return null;
		}

		// The reason is a three bit enumeration that straddles the first two bytes.
		return ((mcs[0] & 0x03) << 1) | (mcs[1] >> 7);
	}

	/// <summary>
	/// True when <paramref name="message"/> is an uncompressed Share Data PDU on <paramref name="ioChannelId"/>, with its
	/// <paramref name="pduType2"/> and the bytes after its header in <paramref name="data"/>. Only the session's own
	/// channel counts, since the bytes of a virtual channel's data could look the same.
	/// </summary>
	/// <param name="ioChannelId">The MCS I/O channel the server sends the session's own PDUs on.</param>
	public static bool TryReadShareData(ReadOnlySpan<byte> message, int ioChannelId, out byte pduType2, out ReadOnlySpan<byte> data)
	{
		pduType2 = 0;
		data = default;

		// A TPKT header and an X.224 Data TPDU, then the MCS PDU. Fast-path messages never carry either of these.
		if (!IsX224Data(message))
		{
			return false;
		}

		ReadOnlySpan<byte> mcs = message[7..];
		if (mcs[0] >> 2 != McsSendDataIndication || mcs.Length < 7)
		{
			return false;
		}

		// initiator (2 bytes), channelId (2, an offset from 0), dataPriority and segmentation (1), then a PER length.
		int channel = BinaryPrimitives.ReadUInt16BigEndian(mcs[3..]);
		if (channel != ioChannelId)
		{
			return false;
		}

		int at = 6;
		int length = mcs[at++];
		if ((length & 0x80) != 0)
		{
			if (mcs.Length <= at)
			{
				return false;
			}

			length = ((length & 0x7F) << 8) | mcs[at++];
		}

		if (length < DataOffset || mcs.Length - at < length)
		{
			return false;
		}

		ReadOnlySpan<byte> share = mcs.Slice(at, length);
		int pduType = BinaryPrimitives.ReadUInt16LittleEndian(share[2..]);
		if ((pduType & ShareControlTypeMask) != ShareControlTypeData || (share[CompressedTypeOffset] & PacketCompressed) != 0)
		{
			return false;
		}

		pduType2 = share[PduType2Offset];
		data = share[DataOffset..];
		return true;
	}

	private static bool IsX224Data(ReadOnlySpan<byte> message) =>
		message.Length >= 9 && message[0] == TpktVersion && message[4] == X224DataLengthIndicator && message[5] == X224DataCode;
}
