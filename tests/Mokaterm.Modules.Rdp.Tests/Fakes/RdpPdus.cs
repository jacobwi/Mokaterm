using System.Buffers.Binary;

namespace Mokaterm.Modules.Rdp.Tests.Fakes;

/// <summary>
/// Builds the slow-path messages a server sends, field by field as MS-RDPBCGR lays them out: a TPKT header, an X.224
/// Data TPDU, an MCS PDU and, inside a Send Data Indication, a Share Data PDU.
/// </summary>
internal static class RdpPdus
{
	/// <summary>The initiator a Windows server writes into its Send Data Indications (the MCS user 1002, sent as 1002 - 1001).</summary>
	private const ushort ServerInitiator = 1;

	/// <summary>The length a TPKT header announces, which is all a reactivation stand-in needs to frame a message.</summary>
	public static nuint? TpktLength(byte[] bytes) => bytes.Length >= 4 ? (nuint)((bytes[2] << 8) | bytes[3]) : null;

	/// <summary>A TPKT header and an X.224 Data TPDU around <paramref name="mcs"/>.</summary>
	public static byte[] X224(ReadOnlySpan<byte> mcs)
	{
		byte[] message = new byte[7 + mcs.Length];
		message[0] = 0x03;
		BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(2), (ushort)message.Length);
		message[4] = 0x02;
		message[5] = 0xF0;
		message[6] = 0x80;
		mcs.CopyTo(message.AsSpan(7));
		return message;
	}

	/// <summary>An MCS Disconnect Provider Ultimatum (2.2.2.3): CHOICE 8 in the top six bits, then the three bit reason.</summary>
	public static byte[] Ultimatum(int reason) => X224([(byte)((8 << 2) | ((reason >> 1) & 0x03)), (byte)((reason & 1) << 7)]);

	/// <summary>An MCS Send Data Indication (CHOICE 26) on <paramref name="channel"/>.</summary>
	/// <param name="longLength">Writes the user data length in two bytes even when one would do, which PER allows.</param>
	public static byte[] SendDataIndication(int channel, ReadOnlySpan<byte> userData, bool longLength = false)
	{
		bool twoBytes = longLength || userData.Length > 0x7F;
		byte[] mcs = new byte[6 + (twoBytes ? 2 : 1) + userData.Length];
		mcs[0] = 26 << 2;
		BinaryPrimitives.WriteUInt16BigEndian(mcs.AsSpan(1), ServerInitiator);
		BinaryPrimitives.WriteUInt16BigEndian(mcs.AsSpan(3), (ushort)channel);
		mcs[5] = 0x70;
		int at = 6;
		if (twoBytes)
		{
			BinaryPrimitives.WriteUInt16BigEndian(mcs.AsSpan(at), (ushort)(0x8000 | userData.Length));
			at += 2;
		}
		else
		{
			mcs[at++] = (byte)userData.Length;
		}

		userData.CopyTo(mcs.AsSpan(at));
		return X224(mcs);
	}

	/// <summary>
	/// TS_SHARECONTROLHEADER with PDUTYPE_DATAPDU, then TS_SHAREDATAHEADER with <paramref name="pduType2"/>, then the body.
	/// </summary>
	/// <param name="pduType">The share control type with its version bits; 0x17 is a data PDU, 0x16 Deactivate All.</param>
	public static byte[] ShareData(byte pduType2, ReadOnlySpan<byte> body, byte compressedType = 0, ushort pduType = 0x17)
	{
		byte[] share = new byte[18 + body.Length];
		BinaryPrimitives.WriteUInt16LittleEndian(share, (ushort)share.Length);
		BinaryPrimitives.WriteUInt16LittleEndian(share.AsSpan(2), pduType);
		BinaryPrimitives.WriteUInt16LittleEndian(share.AsSpan(4), 1002);
		BinaryPrimitives.WriteUInt32LittleEndian(share.AsSpan(6), 0x0001_03EA);
		share[11] = 0x01;
		BinaryPrimitives.WriteUInt16LittleEndian(share.AsSpan(12), (ushort)(body.Length + 4));
		share[14] = pduType2;
		share[15] = compressedType;
		body.CopyTo(share.AsSpan(18));
		return share;
	}

	/// <summary>A Set Error Info PDU (2.2.5.1.1, pduType2 0x2F) carrying <paramref name="code"/>.</summary>
	public static byte[] SetErrorInfo(uint code, int channel = FakeStage.DefaultIoChannel, bool longLength = false, byte compressedType = 0)
	{
		byte[] body = new byte[4];
		BinaryPrimitives.WriteUInt32LittleEndian(body, code);
		return SendDataIndication(channel, ShareData(0x2F, body, compressedType), longLength);
	}

	/// <summary>A Save Session Info PDU (pduType2 0x26), something IronRDP reads and the session goes on after.</summary>
	public static byte[] SaveSessionInfo(int channel = FakeStage.DefaultIoChannel) =>
		SendDataIndication(channel, ShareData(0x26, [0, 0, 0, 0]));

	/// <summary>A Save Session Info PDU carrying <paramref name="infoType"/> and whatever follows it, as Windows pads it.</summary>
	public static byte[] SaveSessionInfo(uint infoType, ReadOnlySpan<byte> infoData, int channel = FakeStage.DefaultIoChannel)
	{
		byte[] body = new byte[4 + infoData.Length];
		BinaryPrimitives.WriteUInt32LittleEndian(body, infoType);
		infoData.CopyTo(body.AsSpan(4));
		return SendDataIndication(channel, ShareData(0x26, body));
	}

	/// <summary>
	/// TS_LOGON_INFO_EXTENDED (2.2.10.1.1.4) with logon errors, and an auto-reconnect cookie before them when asked for,
	/// then the 570 bytes of padding.
	/// </summary>
	public static byte[] LogonErrors(uint type, uint data, bool withCookie = false, int channel = FakeStage.DefaultIoChannel)
	{
		List<byte> fields = [];
		uint present = 0x2;
		if (withCookie)
		{
			present |= 0x1;

			// cbFieldData, then ARC_SC_PRIVATE_PACKET: cbLen 28, Version 1, LogonId, ArcRandomBits.
			fields.AddRange(UInt32(28));
			fields.AddRange(UInt32(28));
			fields.AddRange(UInt32(1));
			fields.AddRange(UInt32(2));
			fields.AddRange(new byte[16]);
		}

		fields.AddRange(UInt32(8));
		fields.AddRange(UInt32(type));
		fields.AddRange(UInt32(data));

		byte[] extended = new byte[6 + fields.Count + 570];
		BinaryPrimitives.WriteUInt16LittleEndian(extended, (ushort)(6 + fields.Count));
		BinaryPrimitives.WriteUInt32LittleEndian(extended.AsSpan(2), present);
		fields.CopyTo(extended, 6);
		return SaveSessionInfo(3, extended, channel);
	}

	/// <summary>
	/// A Server MCS Connect Response (2.2.1.4) that IronRDP accepts for a TLS connection: BER around a GCC Conference
	/// Create Response whose user data holds the core block (with RNS_UD_SC_SKIP_CHANNELJOIN_SUPPORTED, so no channel
	/// joins follow), the network block (I/O channel 1003, no others) and a security block that encrypts nothing.
	/// </summary>
	/// <remarks>The fixed bytes are IronRDP's own test vectors (ironrdp-testsuite-core, mcs.rs and conference_create.rs).</remarks>
	public static byte[] ServerConnectResponse()
	{
		byte[] blocks =
		[
			0x01, 0x0C, 0x10, 0x00, 0x04, 0x00, 0x08, 0x00, 0x01, 0x00, 0x00, 0x00, 0x08, 0x00, 0x00, 0x00,
			0x03, 0x0C, 0x08, 0x00, 0xEB, 0x03, 0x00, 0x00,
			0x02, 0x0C, 0x0C, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
		];
		byte[] response = [0x14, 0x76, 0x0A, 0x01, 0x01, 0x00, 0x01, 0xC0, 0x00, 0x4D, 0x63, 0x44, 0x6E];
		List<byte> conference = [0x00, 0x05, 0x00, 0x14, 0x7C, 0x00, 0x01];
		conference.AddRange(PerLength(response.Length + 2 + blocks.Length));
		conference.AddRange(response);
		conference.AddRange(PerLength(blocks.Length));
		conference.AddRange(blocks);

		byte[] parameters =
		[
			0x30, 0x1A, 0x02, 0x01, 0x22, 0x02, 0x01, 0x03, 0x02, 0x01, 0x00, 0x02, 0x01, 0x01, 0x02, 0x01, 0x00,
			0x02, 0x01, 0x01, 0x02, 0x03, 0x00, 0xFF, 0xF8, 0x02, 0x01, 0x02,
		];
		List<byte> content = [0x0A, 0x01, 0x00, 0x02, 0x01, 0x00, .. parameters, 0x04];
		content.AddRange(BerLength(conference.Count));
		content.AddRange(conference);

		List<byte> mcs = [0x7F, 0x66];
		mcs.AddRange(BerLength(content.Count));
		mcs.AddRange(content);
		return X224([.. mcs]);
	}

	/// <summary>An MCS Attach User Confirm (2.2.1.7) that makes the client user 1007.</summary>
	public static byte[] AttachUserConfirm() => X224([0x2E, 0x00, 0x00, 0x06]);

	private static byte[] UInt32(uint value)
	{
		byte[] bytes = new byte[4];
		BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
		return bytes;
	}

	private static byte[] PerLength(int length) => [(byte)(0x80 | (length >> 8)), (byte)length];

	private static byte[] BerLength(int length) => [0x82, (byte)(length >> 8), (byte)length];
}
