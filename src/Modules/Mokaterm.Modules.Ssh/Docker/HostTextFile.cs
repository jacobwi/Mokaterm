using System.Text;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// A text file read from the docker host for looking at or for editing: its text, and what it takes to write it back
/// unchanged where it was not edited (byte order mark, line endings) and to notice it changed underneath (size, time).
/// Prints its path and size, never its text.
/// </summary>
internal sealed record HostTextFile
{
	public required string Path { get; init; }

	public required string Text { get; init; }

	public long Size { get; init; }

	public DateTimeOffset? Modified { get; init; }

	/// <summary>True when only the start of the file is in <see cref="Text"/>; such a file is never written back.</summary>
	public bool Truncated { get; init; }

	public bool HasByteOrderMark { get; init; }

	public bool UsesCrLf { get; init; }

	/// <summary>True when it was read through sudo.</summary>
	public bool ReadAsRoot { get; init; }

	/// <summary>
	/// The text of <paramref name="bytes"/>, or null for what is not text: a NUL anywhere, or bytes that are not UTF-8.
	/// A file cut at <paramref name="truncated"/> may end inside a character, which is dropped rather than refused.
	/// </summary>
	public static string? Decode(ReadOnlySpan<byte> bytes, bool truncated, out bool hasByteOrderMark)
	{
		hasByteOrderMark = bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]);
		ReadOnlySpan<byte> body = hasByteOrderMark ? bytes[3..] : bytes;
		if (body.Contains((byte)0))
		{
			return null;
		}

		if (truncated)
		{
			body = body[..CompleteLength(body)];
		}

		return IsUtf8(body) ? Encoding.UTF8.GetString(body) : null;
	}

	/// <summary>
	/// The length of <paramref name="bytes"/> without a last character the cut split, found from the lead byte of the
	/// last sequence. Everything before it still has to be valid UTF-8, so a binary file is refused cut or not.
	/// </summary>
	private static int CompleteLength(ReadOnlySpan<byte> bytes)
	{
		for (int back = 1; back <= Math.Min(3, bytes.Length); back++)
		{
			byte lead = bytes[^back];
			if ((lead & 0xC0) == 0x80)
			{
				continue;
			}

			int needed = lead >= 0xF0 ? 4 : lead >= 0xE0 ? 3 : lead >= 0xC0 ? 2 : 1;
			return needed > back ? bytes.Length - back : bytes.Length;
		}

		return bytes.Length;
	}

	/// <summary>The bytes to write for <paramref name="text"/>: the file's own line endings and byte order mark kept.</summary>
	public byte[] Encode(string text)
	{
		ArgumentNullException.ThrowIfNull(text);
		string lines = text.ReplaceLineEndings(UsesCrLf ? "\r\n" : "\n");
		byte[] body = Encoding.UTF8.GetBytes(lines);
		return HasByteOrderMark ? [0xEF, 0xBB, 0xBF, .. body] : body;
	}

	private static bool IsUtf8(ReadOnlySpan<byte> bytes) => System.Text.Unicode.Utf8.IsValid(bytes);

	private bool PrintMembers(StringBuilder builder)
	{
		builder.Append("Path = ").Append(Path).Append(", Size = ").Append(Size).Append(", Truncated = ").Append(Truncated);
		return true;
	}
}
