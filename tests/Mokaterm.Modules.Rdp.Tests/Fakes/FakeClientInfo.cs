using System.Buffers.Binary;
using System.Text;
using Mokaterm.Abstractions.Credentials;

namespace Mokaterm.Modules.Rdp.Tests.Fakes;

/// <summary>
/// The Client Info PDU as a Windows server reads it (MS-RDPBCGR 2.2.1.11): an MCS Send Data Request whose user data is
/// a security header flagged SEC_INFO_PKT and then TS_INFO_PACKET, with the domain, user name and password in UTF-16.
/// </summary>
internal sealed record FakeClientInfo(uint Flags, string Domain, string User, string Password)
{
	/// <summary>INFO_AUTOLOGON: sign in with the name and password in this packet.</summary>
	public const uint Autologon = 0x0000_0008;

	/// <summary>INFO_UNICODE: the strings are UTF-16.</summary>
	public const uint Unicode = 0x0000_0010;

	private const int McsSendDataRequest = 25;
	private const ushort SecInfoPacket = 0x0040;

	public bool SignsInAutomatically => (Flags & Autologon) != 0;

	/// <summary>Reads one whole TPKT message, or throws when it is not a Client Info PDU.</summary>
	public static FakeClientInfo Read(byte[] message)
	{
		ReadOnlySpan<byte> mcs = message.AsSpan(7);
		if (mcs[0] >> 2 != McsSendDataRequest)
		{
			throw new InvalidDataException("Not an MCS Send Data Request.");
		}

		// initiator (2), channelId (2), dataPriority and segmentation (1), then a PER length of one or two bytes.
		int at = 6;
		int length = mcs[at++];
		if ((length & 0x80) != 0)
		{
			length = ((length & 0x7F) << 8) | mcs[at++];
		}

		ReadOnlySpan<byte> data = mcs.Slice(at, length);
		if ((BinaryPrimitives.ReadUInt16LittleEndian(data) & SecInfoPacket) == 0)
		{
			throw new InvalidDataException("The security header is not flagged SEC_INFO_PKT.");
		}

		ReadOnlySpan<byte> info = data[4..];
		uint flags = BinaryPrimitives.ReadUInt32LittleEndian(info[4..]);
		int domainBytes = BinaryPrimitives.ReadUInt16LittleEndian(info[8..]);
		int userBytes = BinaryPrimitives.ReadUInt16LittleEndian(info[10..]);
		int passwordBytes = BinaryPrimitives.ReadUInt16LittleEndian(info[12..]);

		// Each string is followed by a two byte terminator its count leaves out.
		int offset = 18;
		string domain = Encoding.Unicode.GetString(info.Slice(offset, domainBytes));
		offset += domainBytes + 2;
		string user = Encoding.Unicode.GetString(info.Slice(offset, userBytes));
		offset += userBytes + 2;
		string password = Encoding.Unicode.GetString(info.Slice(offset, passwordBytes));
		return new FakeClientInfo(flags, domain, user, password);
	}

	private bool PrintMembers(StringBuilder builder)
	{
		builder
			.Append("Flags = 0x").Append(Flags.ToString("X8", System.Globalization.CultureInfo.InvariantCulture))
			.Append(", Domain = ").Append(Domain)
			.Append(", User = ").Append(User)
			.Append(", Password = ").Append(SecretText.Describe(Password));
		return true;
	}
}
