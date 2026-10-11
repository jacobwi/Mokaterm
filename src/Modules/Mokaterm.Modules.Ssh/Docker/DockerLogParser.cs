namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// Reads <c>docker logs --timestamps</c>. Docker writes the container's stdout to stdout and its stderr to stderr, each
/// line led by a fixed-width RFC 3339 timestamp, so the two streams are put back in order by that timestamp.
/// </summary>
internal static class DockerLogParser
{
	/// <summary>Both streams as one list, oldest first. Lines with the same timestamp keep stdout before stderr.</summary>
	public static IReadOnlyList<DockerLogLine> Merge(string? output, string? error)
	{
		List<DockerLogLine> lines = [.. Lines(output, isError: false), .. Lines(error, isError: true)];

		// A stable sort: equal timestamps keep the order they arrived in.
		return [.. lines.Select((line, index) => (line, index))
			.OrderBy(entry => entry.line.Timestamp, StringComparer.Ordinal)
			.ThenBy(entry => entry.index)
			.Select(entry => entry.line)];
	}

	/// <summary>The lines of one stream. A line without a timestamp in front is kept whole with an empty one.</summary>
	public static IEnumerable<DockerLogLine> Lines(string? text, bool isError)
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
			if (space > 0 && IsTimestamp(line.AsSpan(0, space)))
			{
				yield return new DockerLogLine(line[..space], DockerText.Plain(line[(space + 1)..]), isError);
			}
			else
			{
				yield return new DockerLogLine("", DockerText.Plain(line), isError);
			}
		}
	}

	/// <summary>
	/// <c>compose logs --timestamps</c>: every line led by its container's name and a bar (<c>shop-web-1  | </c>), then
	/// the timestamp. The lines are sorted by timestamp across containers, which is the order they happened in.
	/// </summary>
	public static IReadOnlyList<DockerLogLine> MergeCompose(string? output, string? error)
	{
		List<DockerLogLine> lines = [];
		foreach (DockerLogLine line in Lines(output, isError: false).Concat(Lines(error, isError: true)))
		{
			// Lines() found no timestamp in front, because the name and the bar are there; take them off and look again.
			string text = line.Text;
			int bar = text.IndexOf("| ", StringComparison.Ordinal);
			if (line.Timestamp.Length > 0 || bar <= 0)
			{
				lines.Add(line);
				continue;
			}

			string source = text[..bar].Trim();
			string rest = text[(bar + 2)..];
			int space = rest.IndexOf(' ', StringComparison.Ordinal);
			lines.Add(space > 0 && IsTimestamp(rest.AsSpan(0, space))
				? new DockerLogLine(rest[..space], rest[(space + 1)..], line.IsError) { Source = source }
				: new DockerLogLine("", rest, line.IsError) { Source = source });
		}

		return [.. lines.Select((line, index) => (line, index))
			.OrderBy(entry => entry.line.Timestamp, StringComparer.Ordinal)
			.ThenBy(entry => entry.index)
			.Select(entry => entry.line)];
	}

	/// <summary><c>2024-01-02T03:04:05.123456789Z</c> or with an offset: digits, a T and a colon where they belong.</summary>
	public static bool IsTimestamp(ReadOnlySpan<char> text) =>
		text.Length >= 20
		&& char.IsAsciiDigit(text[0])
		&& text[4] == '-'
		&& text[7] == '-'
		&& text[10] == 'T'
		&& text[13] == ':'
		&& text[16] == ':';
}
