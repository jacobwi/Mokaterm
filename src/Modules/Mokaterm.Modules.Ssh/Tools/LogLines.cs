using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.Modules.Ssh.Tools;

/// <summary>
/// Reads what a log command printed into <see cref="LogLine"/>s: one per line, a leading timestamp taken off when there
/// is one, and the text made plain. <c>docker logs --timestamps</c>, <c>journalctl -o short-iso</c> and a syslog file
/// that writes RFC 3339 all lead with the same shape.
/// </summary>
internal static class LogLines
{
	/// <summary>The lines of one stream. A line without a timestamp in front is kept whole with an empty one.</summary>
	public static IEnumerable<LogLine> Read(string? text, bool isError)
	{
		if (string.IsNullOrEmpty(text))
		{
			yield break;
		}

		string[] lines = text.Split('\n');
		int count = lines.Length;

		// The output ends with a line break, which leaves one empty piece behind it.
		if (count > 0 && lines[^1].Length == 0)
		{
			count--;
		}

		for (int index = 0; index < count; index++)
		{
			string line = lines[index].TrimEnd('\r');
			int space = line.IndexOf(' ', StringComparison.Ordinal);
			yield return space > 0 && IsTimestamp(line.AsSpan(0, space))
				? new LogLine(line[..space], TerminalTextFilter.Plain(line[(space + 1)..]), isError)
				: new LogLine("", TerminalTextFilter.Plain(line), isError);
		}
	}

	/// <summary>
	/// Lines from several streams in the order they happened, by timestamp. A stable sort: lines with the same
	/// timestamp, or none, keep the order they arrived in.
	/// </summary>
	public static IReadOnlyList<LogLine> InOrder(IEnumerable<LogLine> lines)
	{
		ArgumentNullException.ThrowIfNull(lines);
		return [.. lines.Select((line, index) => (line, index))
			.OrderBy(entry => entry.line.Timestamp, StringComparer.Ordinal)
			.ThenBy(entry => entry.index)
			.Select(entry => entry.line)];
	}

	/// <summary><c>2024-01-02T03:04:05.123456789Z</c>, or with an offset: digits, a T and a colon where they belong.</summary>
	public static bool IsTimestamp(ReadOnlySpan<char> text) =>
		text.Length >= 20
		&& char.IsAsciiDigit(text[0])
		&& text[4] == '-'
		&& text[7] == '-'
		&& text[10] == 'T'
		&& text[13] == ':'
		&& text[16] == ':';

	/// <summary>A timestamp that can go on a command line as one word, as a <c>--since</c> does.</summary>
	public static bool IsTimestampWord(string? text) =>
		text is not null
		&& IsTimestamp(text)
		&& !text.Any(character => char.IsWhiteSpace(character) || char.IsControl(character));
}
