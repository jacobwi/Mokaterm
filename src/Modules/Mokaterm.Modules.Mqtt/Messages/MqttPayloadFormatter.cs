using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Mokaterm.Modules.Mqtt.Messages;

/// <summary>
/// Renders a payload for a human: as text, as indented JSON or as a hex dump. Every method caps what it produces,
/// because a page holding a megabyte of text costs the same on a Blazor circuit as sending it twice.
/// </summary>
internal static class MqttPayloadFormatter
{
	/// <summary>Characters the text view renders. Past this the rest is cut and said to be.</summary>
	public const int MaxTextCharacters = 64 * 1024;

	/// <summary>Bytes the hex view dumps. Sixteen per line, so this is 256 lines.</summary>
	public const int MaxHexBytes = 4096;

	/// <summary>Bytes handed to the JSON reader. Anything larger is shown as text instead.</summary>
	public const int MaxJsonBytes = 256 * 1024;

	private const int HexBytesPerLine = 16;

	private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

	/// <summary>The payload as UTF-8 text. Bytes that are not valid UTF-8 become the replacement character.</summary>
	public static string Text(ReadOnlySpan<byte> payload)
	{
		if (payload.Length == 0)
		{
			return "";
		}

		bool cut = payload.Length > MaxTextCharacters;
		string text = Utf8.GetString(cut ? payload[..MaxTextCharacters] : payload);
		return cut ? text + Environment.NewLine + Cut(payload.Length) : text;
	}

	/// <summary>
	/// The payload as indented JSON, or null when it is not JSON. Nesting past the reader's depth limit, a trailing
	/// comma or a bare number all come back as null rather than as an exception.
	/// </summary>
	public static string? Json(ReadOnlySpan<byte> payload)
	{
		if (payload.Length is 0 or > MaxJsonBytes)
		{
			return null;
		}

		// A payload of "12" or "true" is valid JSON that the text view already shows better.
		if (!StartsJson(payload))
		{
			return null;
		}

		try
		{
			using JsonDocument document = JsonDocument.Parse(payload.ToArray(), new JsonDocumentOptions
			{
				CommentHandling = JsonCommentHandling.Skip,
				AllowTrailingCommas = true,
			});

			using MemoryStream buffer = new();
			using (Utf8JsonWriter writer = new(buffer, new JsonWriterOptions { Indented = true, SkipValidation = true }))
			{
				document.WriteTo(writer);
			}

			string text = Utf8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
			return text.Length > MaxTextCharacters
				? text[..MaxTextCharacters] + Environment.NewLine + "... cut here; the indented form is longer than this view holds."
				: text;
		}
		catch (JsonException)
		{
			return null;
		}
	}

	/// <summary>A hex dump with offsets and the printable ASCII beside it, the way <c>hexdump -C</c> writes one.</summary>
	public static string Hex(ReadOnlySpan<byte> payload)
	{
		if (payload.Length == 0)
		{
			return "";
		}

		ReadOnlySpan<byte> shown = payload.Length > MaxHexBytes ? payload[..MaxHexBytes] : payload;
		StringBuilder builder = new(shown.Length * 4);
		for (int offset = 0; offset < shown.Length; offset += HexBytesPerLine)
		{
			ReadOnlySpan<byte> line = shown[offset..Math.Min(offset + HexBytesPerLine, shown.Length)];
			builder.Append(CultureInfo.InvariantCulture, $"{offset:x8}  ");
			for (int index = 0; index < HexBytesPerLine; index++)
			{
				builder.Append(index < line.Length ? line[index].ToString("x2", CultureInfo.InvariantCulture) : "  ");
				builder.Append(index == (HexBytesPerLine / 2) - 1 ? "  " : " ");
			}

			builder.Append(' ');
			foreach (byte value in line)
			{
				builder.Append(value is >= 0x20 and < 0x7f ? (char)value : '.');
			}

			builder.Append('\n');
		}

		if (payload.Length > shown.Length)
		{
			builder.Append(Cut(payload.Length));
		}

		return builder.ToString();
	}

	/// <summary>
	/// One short line for a topic row: the payload as text when it reads as text, otherwise its size. Control
	/// characters become spaces, so a payload cannot break the row it sits in.
	/// </summary>
	public static string Preview(ReadOnlySpan<byte> payload, int fullLength, int maxCharacters)
	{
		if (fullLength == 0)
		{
			return "(empty)";
		}

		if (!LooksLikeText(payload))
		{
			return Size(fullLength);
		}

		string text = Utf8.GetString(payload[..Math.Min(payload.Length, maxCharacters * 4)]);
		StringBuilder builder = new(Math.Min(text.Length, maxCharacters));
		bool lastWasSpace = false;
		foreach (char character in text)
		{
			if (builder.Length >= maxCharacters)
			{
				builder.Append('…');
				break;
			}

			bool space = char.IsControl(character) || char.IsWhiteSpace(character);
			if (space && (lastWasSpace || builder.Length == 0))
			{
				continue;
			}

			builder.Append(space ? ' ' : character);
			lastWasSpace = space;
		}

		string preview = builder.ToString().TrimEnd();
		return preview.Length == 0 ? Size(fullLength) : preview;
	}

	/// <summary>
	/// True when the bytes decode as UTF-8 without a control character other than tab, newline or return. That is
	/// what decides whether the payload viewer opens on text or on hex.
	/// </summary>
	public static bool LooksLikeText(ReadOnlySpan<byte> payload)
	{
		if (payload.Length == 0)
		{
			return true;
		}

		// Only the start is judged: a long payload that begins as text is text, and a cut one can end mid character.
		int limit = Math.Min(payload.Length, 4096);
		foreach (byte value in payload[..limit])
		{
			if (value < 0x20 && value is not ((byte)'\t' or (byte)'\n' or (byte)'\r'))
			{
				return false;
			}
		}

		return true;
	}

	/// <summary><c>512 B</c>, <c>1.5 KB</c>: a payload size for a row or a label.</summary>
	public static string Size(int bytes) => bytes < 1024
		? string.Create(CultureInfo.CurrentCulture, $"{Math.Max(bytes, 0)} B")
		: string.Create(CultureInfo.CurrentCulture, $"{bytes / 1024.0:0.#} KB");

	/// <summary>
	/// Copies a payload out of MQTTnet's sequence, cut to <paramref name="maxBytes"/>. The sequence belongs to the
	/// receive buffer and is reused once the handler returns, so nothing may keep a reference to it.
	/// </summary>
	public static ReadOnlyMemory<byte> Copy(in ReadOnlySequence<byte> payload, long maxBytes, out int fullLength)
	{
		long length = payload.Length;
		fullLength = length > int.MaxValue ? int.MaxValue : (int)length;
		int kept = (int)Math.Min(length, Math.Max(maxBytes, 0));
		if (kept == 0)
		{
			return ReadOnlyMemory<byte>.Empty;
		}

		byte[] buffer = new byte[kept];
		payload.Slice(0, kept).CopyTo(buffer);
		return buffer;
	}

	private static bool StartsJson(ReadOnlySpan<byte> payload)
	{
		foreach (byte value in payload)
		{
			if (value is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
			{
				continue;
			}

			return value is (byte)'{' or (byte)'[';
		}

		return false;
	}

	private static string Cut(int length) =>
		string.Create(CultureInfo.CurrentCulture, $"... cut here; the payload is {Size(length)}.");
}
