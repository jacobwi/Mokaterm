using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.Sessions.Terminal;

/// <summary>
/// The asciicast v2 format: a JSON header line, then one <c>[seconds, "o", "text"]</c> line per chunk of output and a
/// <c>[seconds, "r", "COLSxROWS"]</c> line before the first chunk after a resize. A sink only sees output, so a resize
/// is noticed when the next output arrives, which is when it matters: the server redraws for the new size.
/// </summary>
internal sealed class AsciicastLogFormatter : SessionLogFormatter
{
	/// <summary>Said in the header, the way the text formats say it in their second line.</summary>
	private const string HeaderNotice =
		"What the server printed, with the time each piece arrived, opening with the scrollback that was already on screen. Nothing typed is recorded.";

	/// <summary>The scratch line is let go past this, so one huge replay does not stay allocated for the whole session.</summary>
	private const int KeptScratchBytes = 1024 * 1024;

	// Control characters, quotes and backslashes are still escaped, which is all JSON needs. The default encoder would also
	// turn every non-ASCII character and every '<' into \uXXXX, which suits HTML and only bloats a recording.
	private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

	// asciicast data is text, so the bytes are decoded here. The decoder keeps a character split across two chunks, and
	// what is not UTF-8 becomes U+FFFD, since a JSON string cannot carry it.
	private readonly Decoder _decoder = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false).GetDecoder();

	private ArrayBufferWriter<byte> _line = new();
	private TerminalSize _size;

	public override string Extension => Asciicast.Extension;

	public override byte[] Header(string endpoint, DateTimeOffset startedAt, TerminalSize size)
	{
		_size = size;
		ArrayBufferWriter<byte> header = new();
		using (Utf8JsonWriter json = new(header, WriterOptions))
		{
			json.WriteStartObject();
			json.WriteNumber("version", Asciicast.Version);
			json.WriteNumber("width", size.Columns);
			json.WriteNumber("height", size.Rows);
			json.WriteNumber("timestamp", startedAt.ToUnixTimeSeconds());
			json.WriteString("title", endpoint);
			json.WriteString("mokaterm", HeaderNotice);
			json.WriteEndObject();
		}

		header.Write("\n"u8);
		return header.WrittenSpan.ToArray();
	}

	public override bool Append(ReadOnlySpan<byte> data, TimeSpan elapsed, TerminalSize size, long room, ArrayBufferWriter<byte> output)
	{
		try
		{
			long left = room;
			if (size.IsValid && (size.Columns != _size.Columns || size.Rows != _size.Rows))
			{
				_size = size;
				if (!TryAppend(elapsed, Asciicast.ResizeCode, Asciicast.FormatSize(size.Columns, size.Rows), output, ref left))
				{
					return false;
				}
			}

			char[] chars = ArrayPool<char>.Shared.Rent(Encoding.UTF8.GetMaxCharCount(data.Length + 4));
			try
			{
				int count = _decoder.GetChars(data, chars, flush: false);
				if (count == 0)
				{
					return true;
				}

				ReadOnlySpan<char> text = chars.AsSpan(0, count);
				if (TryAppend(elapsed, Asciicast.OutputCode, text, output, ref left))
				{
					return true;
				}

				// Over the cap: as much of the chunk as fits, still a whole line of JSON, so the file stays playable.
				int fits = LongestFit(elapsed, text, left);
				if (fits > 0)
				{
					Encode(elapsed, Asciicast.OutputCode, text[..fits]);
					output.Write(_line.WrittenSpan);
				}

				return false;
			}
			finally
			{
				ArrayPool<char>.Shared.Return(chars);
			}
		}
		finally
		{
			if (_line.Capacity > KeptScratchBytes)
			{
				_line = new ArrayBufferWriter<byte>();
			}
		}
	}

	public override byte[] LimitNotice(long kilobytes, TimeSpan elapsed)
	{
		Encode(elapsed, Asciicast.OutputCode, LimitText(kilobytes));
		return _line.WrittenSpan.ToArray();
	}

	/// <summary>Seconds since the file opened with six decimals, as asciinema writes them.</summary>
	internal static decimal Seconds(TimeSpan elapsed) =>
		elapsed <= TimeSpan.Zero
			? 0
			: decimal.Round((decimal)elapsed.Ticks / TimeSpan.TicksPerSecond, 6, MidpointRounding.AwayFromZero);

	private bool TryAppend(TimeSpan elapsed, string code, ReadOnlySpan<char> payload, ArrayBufferWriter<byte> output, ref long left)
	{
		Encode(elapsed, code, payload);
		if (_line.WrittenCount > left)
		{
			return false;
		}

		output.Write(_line.WrittenSpan);
		left -= _line.WrittenCount;
		return true;
	}

	/// <summary>
	/// The most characters of <paramref name="text"/> whose line fits in <paramref name="left"/> bytes. Escaping makes the
	/// length of a line a matter of what is in it, so this measures rather than guesses; it runs once per file.
	/// </summary>
	private int LongestFit(TimeSpan elapsed, ReadOnlySpan<char> text, long left)
	{
		int low = 0;
		int high = text.Length;
		while (low < high)
		{
			int middle = low + ((high - low + 1) / 2);
			Encode(elapsed, Asciicast.OutputCode, text[..WholeCharacters(text, middle)]);
			if (_line.WrittenCount <= left)
			{
				low = middle;
			}
			else
			{
				high = middle - 1;
			}
		}

		return WholeCharacters(text, low);
	}

	/// <summary>
	/// <paramref name="length"/>, or one less where that would end on the first half of a surrogate pair, which the JSON
	/// writer refuses to encode.
	/// </summary>
	private static int WholeCharacters(ReadOnlySpan<char> text, int length) =>
		length > 0 && char.IsHighSurrogate(text[length - 1]) ? length - 1 : length;

	private void Encode(TimeSpan elapsed, string code, ReadOnlySpan<char> payload)
	{
		_line.ResetWrittenCount();
		using (Utf8JsonWriter json = new(_line, WriterOptions))
		{
			json.WriteStartArray();
			json.WriteNumberValue(Seconds(elapsed));
			json.WriteStringValue(code);
			json.WriteStringValue(payload);
			json.WriteEndArray();
		}

		_line.Write("\n"u8);
	}
}
