using System.Buffers;
using System.Text;

namespace Mokaterm.Abstractions.Terminal;

/// <summary>
/// Moves bytes between the terminal view, which is always UTF-8, and a connection that may speak a code page. Both
/// directions keep their converter state, so a character split across two reads still arrives whole: a read ends
/// wherever the socket or the driver happened to have bytes.
/// </summary>
public sealed class TerminalTextCodec
{
	private const int CharBufferLength = 4096;

	private readonly Decoder? _remoteDecoder;
	private readonly Encoder? _utf8Encoder;
	private readonly Decoder? _utf8Decoder;
	private readonly Encoder? _remoteEncoder;

	public TerminalTextCodec(Encoding encoding)
	{
		ArgumentNullException.ThrowIfNull(encoding);
		Encoding = encoding;
		if (TerminalEncodings.IsUtf8(encoding))
		{
			return;
		}

		_remoteDecoder = encoding.GetDecoder();
		_utf8Encoder = Encoding.UTF8.GetEncoder();
		_utf8Decoder = Encoding.UTF8.GetDecoder();
		_remoteEncoder = encoding.GetEncoder();
	}

	public Encoding Encoding { get; }

	/// <summary>True when the remote side is UTF-8 and bytes pass through untouched.</summary>
	public bool IsPassthrough => _remoteDecoder is null;

	/// <summary>Turns bytes from the remote side into the UTF-8 the terminal view expects.</summary>
	public void ToTerminal(ReadOnlySpan<byte> input, IBufferWriter<byte> output) =>
		Convert(input, output, _remoteDecoder, _utf8Encoder);

	/// <summary>Turns UTF-8 typed in the view into the bytes the remote side expects.</summary>
	public void FromTerminal(ReadOnlySpan<byte> input, IBufferWriter<byte> output) =>
		Convert(input, output, _utf8Decoder, _remoteEncoder);

	private static void Convert(ReadOnlySpan<byte> input, IBufferWriter<byte> output, Decoder? decoder, Encoder? encoder)
	{
		ArgumentNullException.ThrowIfNull(output);
		if (decoder is null || encoder is null)
		{
			output.Write(input);
			return;
		}

		char[] chars = ArrayPool<char>.Shared.Rent(CharBufferLength);
		try
		{
			while (!input.IsEmpty)
			{
				decoder.Convert(input, chars, flush: false, out int bytesUsed, out int charsWritten, out _);
				Encode(chars.AsSpan(0, charsWritten), output, encoder);

				// An incomplete character at the end of the input is held by the decoder, not looped over again.
				if (bytesUsed == 0)
				{
					return;
				}

				input = input[bytesUsed..];
			}
		}
		finally
		{
			ArrayPool<char>.Shared.Return(chars);
		}
	}

	private static void Encode(ReadOnlySpan<char> chars, IBufferWriter<byte> output, Encoder encoder)
	{
		while (!chars.IsEmpty)
		{
			Span<byte> span = output.GetSpan((chars.Length * 4) + 16);
			encoder.Convert(chars, span, flush: false, out int charsUsed, out int bytesWritten, out _);
			output.Advance(bytesWritten);
			if (charsUsed == 0)
			{
				return;
			}

			chars = chars[charsUsed..];
		}
	}
}
