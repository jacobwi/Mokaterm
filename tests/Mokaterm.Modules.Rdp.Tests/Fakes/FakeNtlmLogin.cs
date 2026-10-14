using System.Buffers.Binary;
using System.Formats.Asn1;
using System.Text;

namespace Mokaterm.Modules.Rdp.Tests.Fakes;

/// <summary>
/// The server half of CredSSP over NTLM, as far as reading the login a client sends: it answers the client's NTLM
/// negotiate with a challenge, reads the NTLM authenticate that comes back and turns it down with
/// STATUS_LOGON_FAILURE, which is what a Windows server says for a wrong password. The authenticate message is where the
/// user name and the domain travel, so this is how a test sees what a typed name turns into on the wire.
/// </summary>
internal static class FakeNtlmLogin
{
	/// <summary>STATUS_LOGON_FAILURE as the signed integer a TSRequest's errorCode carries.</summary>
	public const int LogonFailure = unchecked((int)0xC000006D);

	private static readonly byte[] Signature = "NTLMSSP\0"u8.ToArray();

	/// <summary>Runs the exchange on <paramref name="tls"/> and returns the domain and user the client sent.</summary>
	public static async Task<(string Domain, string User)> RefuseAsync(Stream tls, CancellationToken cancellationToken)
	{
		byte[] negotiate = ReadNegoToken(await ReadDerAsync(tls, cancellationToken));
		RequireNtlm(negotiate, 1);
		uint flags = BinaryPrimitives.ReadUInt32LittleEndian(negotiate.AsSpan(12));
		await tls.WriteAsync(TsRequest(Challenge(flags)), cancellationToken);

		byte[] authenticate = ReadNegoToken(await ReadDerAsync(tls, cancellationToken));
		RequireNtlm(authenticate, 3);
		string domain = Field(authenticate, 28);
		string user = Field(authenticate, 36);
		await tls.WriteAsync(ErrorTsRequest(LogonFailure), cancellationToken);
		return (domain, user);
	}

	/// <summary>Reads one DER element, whose length is in its own header.</summary>
	private static async Task<byte[]> ReadDerAsync(Stream stream, CancellationToken cancellationToken)
	{
		byte[] head = new byte[2];
		await stream.ReadExactlyAsync(head, cancellationToken);
		int length = head[1];
		byte[] lengthBytes = [];
		if ((length & 0x80) != 0)
		{
			lengthBytes = new byte[length & 0x7F];
			await stream.ReadExactlyAsync(lengthBytes, cancellationToken);
			length = 0;
			foreach (byte part in lengthBytes)
			{
				length = (length << 8) | part;
			}
		}

		byte[] body = new byte[length];
		await stream.ReadExactlyAsync(body, cancellationToken);
		return [.. head, .. lengthBytes, .. body];
	}

	/// <summary>The first token in a TSRequest's negoTokens.</summary>
	private static byte[] ReadNegoToken(byte[] tsRequest)
	{
		AsnReader request = new AsnReader(tsRequest, AsnEncodingRules.DER).ReadSequence();
		while (request.HasData)
		{
			Asn1Tag tag = request.PeekTag();
			AsnReader field = request.ReadSequence(tag);
			if (tag.TagValue != 1)
			{
				continue;
			}

			AsnReader token = field.ReadSequence().ReadSequence().ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0));
			return token.ReadOctetString();
		}

		throw new InvalidDataException("The TSRequest carried no NTLM token.");
	}

	private static void RequireNtlm(byte[] message, int type)
	{
		if (message.Length < 12 || !message.AsSpan(0, 8).SequenceEqual(Signature) || BinaryPrimitives.ReadInt32LittleEndian(message.AsSpan(8)) != type)
		{
			throw new InvalidDataException($"Expected a raw NTLM message of type {type}.");
		}
	}

	private static string Field(byte[] message, int offset)
	{
		int length = BinaryPrimitives.ReadUInt16LittleEndian(message.AsSpan(offset));
		int start = BinaryPrimitives.ReadInt32LittleEndian(message.AsSpan(offset + 4));
		return Encoding.Unicode.GetString(message, start, length);
	}

	private static byte[] TsRequest(byte[] token)
	{
		AsnWriter writer = new(AsnEncodingRules.DER);
		using (writer.PushSequence())
		{
			using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0)))
			{
				writer.WriteInteger(6);
			}

			using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 1)))
			using (writer.PushSequence())
			using (writer.PushSequence())
			using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0)))
			{
				writer.WriteOctetString(token);
			}
		}

		return writer.Encode();
	}

	private static byte[] ErrorTsRequest(int status)
	{
		AsnWriter writer = new(AsnEncodingRules.DER);
		using (writer.PushSequence())
		{
			using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0)))
			{
				writer.WriteInteger(6);
			}

			using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 4)))
			{
				writer.WriteInteger(status);
			}
		}

		return writer.Encode();
	}

	/// <summary>An NTLM challenge from a workgroup PC called TEST, answering whatever the client offered.</summary>
	private static byte[] Challenge(uint clientFlags)
	{
		const uint TargetInfo = 0x00800000;
		const uint TargetTypeServer = 0x00020000;
		byte[] target = Encoding.Unicode.GetBytes("TEST");
		using MemoryStream info = new();
		WritePair(info, 2, target);
		WritePair(info, 1, target);
		WritePair(info, 4, Encoding.Unicode.GetBytes("test"));
		WritePair(info, 3, Encoding.Unicode.GetBytes("test"));
		byte[] time = new byte[8];
		BinaryPrimitives.WriteInt64LittleEndian(time, DateTime.UtcNow.ToFileTimeUtc());
		WritePair(info, 7, time);
		WritePair(info, 0, []);
		byte[] targetInfo = info.ToArray();

		const int Header = 56;
		byte[] message = new byte[Header + target.Length + targetInfo.Length];
		Signature.CopyTo(message, 0);
		BinaryPrimitives.WriteInt32LittleEndian(message.AsSpan(8), 2);
		WriteField(message, 12, target.Length, Header);
		BinaryPrimitives.WriteUInt32LittleEndian(message.AsSpan(20), clientFlags | TargetInfo | TargetTypeServer);
		new Random(7).NextBytes(message.AsSpan(24, 8));
		WriteField(message, 40, targetInfo.Length, Header + target.Length);

		// Version: Windows 10, build 19041, NTLM revision 15.
		message[48] = 10;
		message[49] = 0;
		BinaryPrimitives.WriteUInt16LittleEndian(message.AsSpan(50), 19041);
		message[55] = 15;
		target.CopyTo(message, Header);
		targetInfo.CopyTo(message, Header + target.Length);
		return message;
	}

	private static void WriteField(byte[] message, int offset, int length, int start)
	{
		BinaryPrimitives.WriteUInt16LittleEndian(message.AsSpan(offset), (ushort)length);
		BinaryPrimitives.WriteUInt16LittleEndian(message.AsSpan(offset + 2), (ushort)length);
		BinaryPrimitives.WriteInt32LittleEndian(message.AsSpan(offset + 4), start);
	}

	private static void WritePair(Stream stream, ushort id, byte[] value)
	{
		Span<byte> header = stackalloc byte[4];
		BinaryPrimitives.WriteUInt16LittleEndian(header, id);
		BinaryPrimitives.WriteUInt16LittleEndian(header[2..], (ushort)value.Length);
		stream.Write(header);
		stream.Write(value);
	}
}
