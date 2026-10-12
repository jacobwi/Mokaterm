using System.Buffers;
using System.Text;
using System.Text.Unicode;
using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.Abstractions.FileSystem;

/// <summary>
/// A remote file read as text, plus what it takes to write it back unchanged: its byte order mark, its line ending and
/// whether it ended with one. A text area hands back line feeds whatever the file used, so without this a file with
/// CRLF endings would come back rewritten from top to bottom the first time it is saved. These are the text rules of
/// every view that reads or edits a remote file, the file browser's editor and the Docker manager's files alike.
/// </summary>
public sealed class RemoteTextFile
{
	private const string Lf = "\n";
	private const string CrLf = "\r\n";
	private const string Cr = "\r";

	// The first buffer and the most one read asks for. Most files are far below any cap, so the buffer starts small and
	// doubles, rather than taking the whole cap for a ten line file.
	private const int ChunkBytes = 64 * 1024;

	// Throwing rather than substituting, both ways: a replacement character would be written back as itself and quietly
	// destroy whatever the server had there.
	private static readonly UTF8Encoding Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

	private RemoteTextFile(string text, bool hasBom, string lineEnding, bool endsWithNewLine, bool isCut)
	{
		Text = text;
		HasBom = hasBom;
		LineEnding = lineEnding;
		EndsWithNewLine = endsWithNewLine;
		IsCut = isCut;
	}

	/// <summary>The contents with every line ending normalized to a line feed, which is what a text area works in.</summary>
	public string Text { get; }

	/// <summary>True when the file starts with the UTF-8 byte order mark, which <see cref="ToBytes"/> puts back.</summary>
	public bool HasBom { get; }

	/// <summary>What the file used: a line feed, a carriage return and line feed, or a lone carriage return.</summary>
	public string LineEnding { get; }

	/// <summary>
	/// True when the last line ends with a line ending. An empty file counts as ending with one, since it has no line yet
	/// and a text file's lines are expected to end.
	/// </summary>
	public bool EndsWithNewLine { get; }

	/// <summary>
	/// True when only the start of the file is in <see cref="Text"/>, as <see cref="ReadStartAsync"/> leaves a longer
	/// one. Such a file is for showing only, and <see cref="ToBytes"/> refuses it.
	/// </summary>
	public bool IsCut { get; }

	private static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];

	/// <summary>
	/// Reads at most <paramref name="maxBytes"/> and refuses anything that could not be handed back unchanged, a longer
	/// file included. It asks for one byte past the cap, because a stream over a network need not report a length, and a
	/// file of exactly the cap must still open.
	/// </summary>
	public static Task<(RemoteTextFile? File, TextFileRefusal Refusal)> ReadAsync(
		Stream source,
		int maxBytes,
		CancellationToken cancellationToken = default) =>
		ReadCoreAsync(source, maxBytes, keepStart: false, cancellationToken);

	/// <summary>
	/// Like <see cref="ReadAsync"/>, but a file longer than <paramref name="maxBytes"/> keeps its start, marked
	/// <see cref="IsCut"/>, instead of being refused: for looking at a file, never for writing it back. A character the
	/// cut split is dropped, and what comes before it must still be text.
	/// </summary>
	public static Task<(RemoteTextFile? File, TextFileRefusal Refusal)> ReadStartAsync(
		Stream source,
		int maxBytes,
		CancellationToken cancellationToken = default) =>
		ReadCoreAsync(source, maxBytes, keepStart: true, cancellationToken);

	/// <summary>The text of a whole file, or the reason it is not text.</summary>
	public static (RemoteTextFile? File, TextFileRefusal Refusal) FromBytes(ReadOnlySpan<byte> bytes) => Decode(bytes, cut: false);

	/// <summary>
	/// The ending the file mostly uses. A file holding both is written back with whichever it had more of, because one
	/// stray ending is not a reason to rewrite every other line.
	/// </summary>
	public static string DetectLineEnding(string text)
	{
		ArgumentNullException.ThrowIfNull(text);
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

	/// <summary>The bytes to write back, with the line ending, the final newline and the byte order mark restored.</summary>
	/// <exception cref="InvalidOperationException">Only the start of the file was read (<see cref="IsCut"/>).</exception>
	/// <exception cref="EncoderFallbackException">The text holds a lone surrogate, which has no UTF-8 form.</exception>
	public byte[] ToBytes(string text)
	{
		ArgumentNullException.ThrowIfNull(text);
		if (IsCut)
		{
			throw new InvalidOperationException("Only the start of this file was read, so it cannot be written back.");
		}

		string body = LineEnding == Lf ? text : text.Replace(Lf, LineEnding, StringComparison.Ordinal);

		// An emptied file stays empty rather than becoming a lone line ending.
		if (body.Length > 0)
		{
			if (EndsWithNewLine && !body.EndsWith(LineEnding, StringComparison.Ordinal))
			{
				body += LineEnding;
			}
			else if (!EndsWithNewLine && body.EndsWith(LineEnding, StringComparison.Ordinal))
			{
				// The file had no final newline, so adding one would show up as a change nobody made.
				body = body[..^LineEnding.Length];
			}
		}

		int start = HasBom ? Utf8Bom.Length : 0;
		byte[] result = new byte[start + Strict.GetByteCount(body)];
		Utf8Bom[..start].CopyTo(result);
		Strict.GetBytes(body, result.AsSpan(start));
		return result;
	}

	private static async Task<(RemoteTextFile? File, TextFileRefusal Refusal)> ReadCoreAsync(
		Stream source,
		int maxBytes,
		bool keepStart,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(source);
		ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);
		ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(maxBytes, Array.MaxLength);

		int limit = maxBytes + 1;
		byte[] buffer = ArrayPool<byte>.Shared.Rent(Math.Min(limit, ChunkBytes));
		int length = 0;
		try
		{
			while (length < limit)
			{
				if (length == buffer.Length)
				{
					buffer = Grow(buffer, length, (int)Math.Min(limit, buffer.Length * 2L));
				}

				int want = Math.Min(Math.Min(buffer.Length, limit) - length, ChunkBytes);
				int read = await source.ReadAsync(buffer.AsMemory(length, want), cancellationToken);
				if (read == 0)
				{
					break;
				}

				length += read;
			}

			bool longer = length > maxBytes;
			if (longer && !keepStart)
			{
				return (null, TextFileRefusal.TooLarge);
			}

			return Decode(buffer.AsSpan(0, Math.Min(length, maxBytes)), longer);
		}
		finally
		{
			// The file may be an env file, which is where passwords live, so its bytes do not stay behind in the pool.
			ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
		}
	}

	private static byte[] Grow(byte[] buffer, int length, int size)
	{
		byte[] larger = ArrayPool<byte>.Shared.Rent(size);
		buffer.AsSpan(0, length).CopyTo(larger);
		ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
		return larger;
	}

	private static (RemoteTextFile? File, TextFileRefusal Refusal) Decode(ReadOnlySpan<byte> bytes, bool cut)
	{
		bool hasBom = bytes.StartsWith(Utf8Bom);
		ReadOnlySpan<byte> body = hasBom ? bytes[Utf8Bom.Length..] : bytes;
		if (body.Contains((byte)0))
		{
			return (null, TextFileRefusal.Binary);
		}

		if (cut)
		{
			// Only the character the cut split goes: what comes before it still has to be valid UTF-8, so a binary file is
			// refused cut or not.
			body = body[..Utf8Cut.CompleteLength(body)];
		}

		if (!Utf8.IsValid(body))
		{
			return (null, TextFileRefusal.NotUtf8);
		}

		string text = Strict.GetString(body);
		string lineEnding = DetectLineEnding(text);
		bool endsWithNewLine = text.Length == 0 || text.EndsWith(Lf, StringComparison.Ordinal) || text.EndsWith(Cr, StringComparison.Ordinal);

		// Only CR and LF are line endings here. Form feeds, NEL and the Unicode line and paragraph separators are text a
		// file holds, and turning them into line feeds would rewrite them on the next save.
		string normalized = text.Replace(CrLf, Lf, StringComparison.Ordinal).Replace(Cr, Lf, StringComparison.Ordinal);
		return (new RemoteTextFile(normalized, hasBom, lineEnding, endsWithNewLine, cut), TextFileRefusal.None);
	}
}
