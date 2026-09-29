using System.Text;

namespace Mokaterm.UI.FileBrowser.Editing;

/// <summary>Why a file cannot be opened in the editor.</summary>
internal enum TextFileRefusal
{
	None,

	/// <summary>Bigger than the cap, so opening it would be a piece of a file that saving would truncate.</summary>
	TooLarge,

	/// <summary>Holds a NUL, so it is not text and an editor would corrupt it.</summary>
	Binary,

	/// <summary>Not valid UTF-8. Decoding with replacement characters would write those characters back.</summary>
	NotUtf8,
}

/// <summary>
/// A remote file read into the editor, plus what it takes to write it back unchanged: the encoding it arrived in, its
/// line ending and whether it ended with one. A text area hands back line feeds whatever the file used, so without
/// this a file with CRLF endings would come back rewritten from top to bottom the first time it is saved.
/// </summary>
internal sealed class RemoteTextFile
{
	private const string Lf = "\n";
	private const string CrLf = "\r\n";
	private const string Cr = "\r";

	private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

	private RemoteTextFile(string text, Encoding encoding, bool hasBom, string lineEnding, bool endsWithNewLine)
	{
		Text = text;
		Encoding = encoding;
		HasBom = hasBom;
		LineEnding = lineEnding;
		EndsWithNewLine = endsWithNewLine;
	}

	/// <summary>The contents with every line ending normalized to a line feed, which is what a text area works in.</summary>
	public string Text { get; }

	public Encoding Encoding { get; }

	public bool HasBom { get; }

	/// <summary>What the file used: a line feed, a carriage return and line feed, or a lone carriage return.</summary>
	public string LineEnding { get; }

	public bool EndsWithNewLine { get; }

	/// <summary>
	/// Reads at most <paramref name="maxBytes"/> and refuses anything the editor could not hand back unchanged. It asks
	/// for one byte past the cap, because a stream over a network need not report a length, and a file of exactly the
	/// cap must still open.
	/// </summary>
	public static async Task<(RemoteTextFile? File, TextFileRefusal Refusal)> ReadAsync(
		Stream source,
		int maxBytes,
		CancellationToken cancellationToken = default)
	{
		using MemoryStream buffer = new();
		byte[] chunk = new byte[64 * 1024];
		while (buffer.Length <= maxBytes)
		{
			int want = (int)Math.Min(chunk.Length, maxBytes + 1 - buffer.Length);
			int read = await source.ReadAsync(chunk.AsMemory(0, want), cancellationToken);
			if (read == 0)
			{
				break;
			}

			buffer.Write(chunk, 0, read);
		}

		if (buffer.Length > maxBytes)
		{
			return (null, TextFileRefusal.TooLarge);
		}

		byte[] bytes = buffer.ToArray();
		RemoteTextFile? file = FromBytes(bytes, out TextFileRefusal refusal);
		return (file, refusal);
	}

	/// <summary>The bytes to write back, with the line ending, the trailing newline and the byte order mark restored.</summary>
	public byte[] ToBytes(string text)
	{
		string body = LineEnding == Lf ? text : text.Replace(Lf, LineEnding, StringComparison.Ordinal);
		if (EndsWithNewLine && !body.EndsWith(LineEnding, StringComparison.Ordinal))
		{
			body += LineEnding;
		}
		else if (!EndsWithNewLine && body.EndsWith(LineEnding, StringComparison.Ordinal))
		{
			// The file had no final newline, so adding one would show up as a change nobody made.
			body = body[..^LineEnding.Length];
		}

		byte[] encoded = Encoding.GetBytes(body);
		if (!HasBom)
		{
			return encoded;
		}

		byte[] result = new byte[Utf8Bom.Length + encoded.Length];
		Utf8Bom.CopyTo(result, 0);
		encoded.CopyTo(result, Utf8Bom.Length);
		return result;
	}

	internal static RemoteTextFile? FromBytes(byte[] bytes, out TextFileRefusal refusal)
	{
		if (Array.IndexOf(bytes, (byte)0) >= 0)
		{
			refusal = TextFileRefusal.Binary;
			return null;
		}

		bool hasBom = bytes.Length >= Utf8Bom.Length && bytes.AsSpan(0, Utf8Bom.Length).SequenceEqual(Utf8Bom);
		ReadOnlySpan<byte> body = hasBom ? bytes.AsSpan(Utf8Bom.Length) : bytes;

		// Throwing on invalid bytes rather than substituting: a replacement character would be written back as itself
		// and quietly destroy whatever the server had there.
		UTF8Encoding strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
		string text;
		try
		{
			text = strict.GetString(body);
		}
		catch (DecoderFallbackException)
		{
			refusal = TextFileRefusal.NotUtf8;
			return null;
		}

		refusal = TextFileRefusal.None;
		string lineEnding = DetectLineEnding(text);
		bool endsWithNewLine = text.EndsWith(Lf, StringComparison.Ordinal) || text.EndsWith(Cr, StringComparison.Ordinal);
		string normalized = text.Replace(CrLf, Lf, StringComparison.Ordinal).Replace(Cr, Lf, StringComparison.Ordinal);
		return new RemoteTextFile(normalized, strict, hasBom, lineEnding, endsWithNewLine);
	}

	/// <summary>
	/// The ending the file mostly uses. A file holding both is written back with whichever it had more of, because one
	/// stray ending is not a reason to rewrite every other line.
	/// </summary>
	internal static string DetectLineEnding(string text)
	{
		int crlf = 0;
		int lf = 0;
		int cr = 0;
		for (int index = 0; index < text.Length; index++)
		{
			if (text[index] == '\r')
			{
				if (index + 1 < text.Length && text[index + 1] == '\n')
				{
					crlf++;
					index++;
				}
				else
				{
					cr++;
				}
			}
			else if (text[index] == '\n')
			{
				lf++;
			}
		}

		if (crlf > lf && crlf >= cr)
		{
			return CrLf;
		}

		return cr > lf && cr > crlf ? Cr : Lf;
	}
}
