using Mokaterm.Modules.Ssh.Tools;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// Reads <c>docker logs --timestamps</c>. Docker writes the container's stdout to stdout and its stderr to stderr, each
/// line led by a fixed-width RFC 3339 timestamp, so the two streams are put back in order by that timestamp.
/// </summary>
internal static class DockerLogParser
{
	/// <summary>Both streams as one list, oldest first. Lines with the same timestamp keep stdout before stderr.</summary>
	public static IReadOnlyList<LogLine> Merge(string? output, string? error) =>
		LogLines.InOrder(LogLines.Read(output, isError: false).Concat(LogLines.Read(error, isError: true)));

	/// <summary>
	/// <c>compose logs --timestamps</c>: every line led by its container's name and a bar (<c>shop-web-1  | </c>), then
	/// the timestamp. The lines are sorted by timestamp across containers, which is the order they happened in.
	/// </summary>
	public static IReadOnlyList<LogLine> MergeCompose(string? output, string? error)
	{
		List<LogLine> lines = [];
		foreach (LogLine line in LogLines.Read(output, isError: false).Concat(LogLines.Read(error, isError: true)))
		{
			// No timestamp was found in front, because the name and the bar are there; take them off and look again.
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
			lines.Add(space > 0 && LogLines.IsTimestamp(rest.AsSpan(0, space))
				? new LogLine(rest[..space], rest[(space + 1)..], line.IsError) { Source = source }
				: new LogLine("", rest, line.IsError) { Source = source });
		}

		return LogLines.InOrder(lines);
	}
}
