using System.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace Mokaterm.Core.Keys;

/// <summary>
/// The SSH wire encoding from RFC 4251: 32 bit big endian lengths in front of every field. Public key blobs,
/// private key blobs and the OpenSSH key container are all built from it.
/// </summary>
internal static class SshWire
{
	public static void WriteUInt32(IBufferWriter<byte> writer, uint value)
	{
		Span<byte> span = writer.GetSpan(4);
		BinaryPrimitives.WriteUInt32BigEndian(span, value);
		writer.Advance(4);
	}

	public static void WriteString(IBufferWriter<byte> writer, ReadOnlySpan<byte> value)
	{
		WriteUInt32(writer, (uint)value.Length);
		Span<byte> span = writer.GetSpan(value.Length);
		value.CopyTo(span);
		writer.Advance(value.Length);
	}

	public static void WriteString(IBufferWriter<byte> writer, string value)
	{
		int length = Encoding.UTF8.GetByteCount(value);
		WriteUInt32(writer, (uint)length);
		Span<byte> span = writer.GetSpan(length);
		Encoding.UTF8.GetBytes(value, span);
		writer.Advance(length);
	}

	public static void WriteBytes(IBufferWriter<byte> writer, ReadOnlySpan<byte> value)
	{
		Span<byte> span = writer.GetSpan(value.Length);
		value.CopyTo(span);
		writer.Advance(value.Length);
	}

	/// <summary>
	/// Writes an unsigned big endian number as an SSH mpint: leading zero bytes dropped, and a zero byte put back
	/// when the top bit is set so the value stays positive.
	/// </summary>
	public static void WriteMpInt(IBufferWriter<byte> writer, ReadOnlySpan<byte> magnitude)
	{
		int start = 0;
		while (start < magnitude.Length && magnitude[start] == 0)
		{
			start++;
		}

		ReadOnlySpan<byte> trimmed = magnitude[start..];
		if (trimmed.IsEmpty)
		{
			WriteUInt32(writer, 0);
			return;
		}

		bool pad = (trimmed[0] & 0x80) != 0;
		WriteUInt32(writer, (uint)(trimmed.Length + (pad ? 1 : 0)));
		if (pad)
		{
			Span<byte> zero = writer.GetSpan(1);
			zero[0] = 0;
			writer.Advance(1);
		}

		WriteBytes(writer, trimmed);
	}
}
