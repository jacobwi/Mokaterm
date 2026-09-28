using System.Buffers.Binary;
using System.Text;

namespace Mokaterm.Modules.Vnc.Protocol;

/// <summary>
/// Reads and writes the big-endian fields of the RFB handshake. Every read takes exactly the bytes it needs, so
/// nothing of the session that follows is swallowed into a buffer the relay cannot see.
/// </summary>
internal sealed class RfbWire
{
	private const uint MaxReasonLength = 8 * 1024;

	private static readonly Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

	private readonly byte[] _scratch = new byte[4];

	public RfbWire(Stream stream) => Stream = stream;

	/// <summary>The stream under the handshake. Replaced by the TLS stream when a VeNCrypt subtype upgrades.</summary>
	public Stream Stream { get; set; }

	/// <summary>UTF-8 when the bytes are valid UTF-8, Latin-1 otherwise, which is what RFB strings officially are.</summary>
	public static string DecodeText(ReadOnlySpan<byte> text)
	{
		try
		{
			return StrictUtf8.GetString(text);
		}
		catch (DecoderFallbackException)
		{
			return Encoding.Latin1.GetString(text);
		}
	}

	public ValueTask ReadExactAsync(Memory<byte> buffer, CancellationToken cancellationToken) =>
		Stream.ReadExactlyAsync(buffer, cancellationToken);

	public async ValueTask<byte[]> ReadBytesAsync(int count, CancellationToken cancellationToken)
	{
		byte[] buffer = new byte[count];
		await Stream.ReadExactlyAsync(buffer, cancellationToken);
		return buffer;
	}

	public async ValueTask<byte> ReadByteAsync(CancellationToken cancellationToken)
	{
		await Stream.ReadExactlyAsync(_scratch.AsMemory(0, 1), cancellationToken);
		return _scratch[0];
	}

	public async ValueTask<ushort> ReadUInt16Async(CancellationToken cancellationToken)
	{
		await Stream.ReadExactlyAsync(_scratch.AsMemory(0, 2), cancellationToken);
		return BinaryPrimitives.ReadUInt16BigEndian(_scratch);
	}

	public async ValueTask<uint> ReadUInt32Async(CancellationToken cancellationToken)
	{
		await Stream.ReadExactlyAsync(_scratch.AsMemory(0, 4), cancellationToken);
		return BinaryPrimitives.ReadUInt32BigEndian(_scratch);
	}

	/// <summary>Reads a length-prefixed reason string, empty when the server sends none.</summary>
	public async ValueTask<string> ReadReasonAsync(CancellationToken cancellationToken)
	{
		uint length = await ReadUInt32Async(cancellationToken);
		if (length is 0 or > MaxReasonLength)
		{
			return "";
		}

		byte[] text = await ReadBytesAsync((int)length, cancellationToken);
		return DecodeText(text);
	}

	public async ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
	{
		await Stream.WriteAsync(data, cancellationToken);
		await Stream.FlushAsync(cancellationToken);
	}

	public ValueTask WriteByteAsync(byte value, CancellationToken cancellationToken)
	{
		_scratch[0] = value;
		return WriteAsync(_scratch.AsMemory(0, 1), cancellationToken);
	}

	public ValueTask WriteUInt32Async(uint value, CancellationToken cancellationToken)
	{
		BinaryPrimitives.WriteUInt32BigEndian(_scratch, value);
		return WriteAsync(_scratch.AsMemory(0, 4), cancellationToken);
	}
}
