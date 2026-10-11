namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// The lines a log view holds while it follows a container. Following polls <c>docker logs --since</c> with the newest
/// timestamp seen, and <c>--since</c> includes lines at that very instant, so the lines already shown at that timestamp
/// are counted and skipped once rather than shown twice.
/// </summary>
internal sealed class DockerLogTail
{
	/// <summary>The most lines a view keeps; the oldest go first.</summary>
	public const int MaxLines = 10_000;

	private readonly List<DockerLogLine> _lines = [];
	private int _atNewest;

	public IReadOnlyList<DockerLogLine> Lines => _lines;

	/// <summary>The newest timestamp seen, for the next <c>--since</c>; empty until a line with one arrived.</summary>
	public string Newest { get; private set; } = "";

	/// <summary>Bumped by every change, so a view can tell whether to draw again.</summary>
	public int Version { get; private set; }

	/// <summary>Starts over with <paramref name="lines"/>, as a fresh tail does.</summary>
	public void Reset(IReadOnlyList<DockerLogLine> lines)
	{
		ArgumentNullException.ThrowIfNull(lines);
		_lines.Clear();
		Newest = "";
		_atNewest = 0;
		Add(lines, skipSeen: false);
	}

	/// <summary>Adds what a <c>--since Newest</c> poll printed, leaving out what is already here.</summary>
	/// <returns>How many lines were new.</returns>
	public int Append(IReadOnlyList<DockerLogLine> lines)
	{
		ArgumentNullException.ThrowIfNull(lines);
		return Add(lines, skipSeen: true);
	}

	private int Add(IReadOnlyList<DockerLogLine> lines, bool skipSeen)
	{
		int added = 0;
		int toSkip = skipSeen ? _atNewest : 0;
		foreach (DockerLogLine line in lines)
		{
			if (skipSeen && line.Timestamp.Length > 0 && Newest.Length > 0)
			{
				int order = string.CompareOrdinal(line.Timestamp, Newest);
				if (order < 0)
				{
					continue;
				}

				if (order == 0 && toSkip > 0)
				{
					toSkip--;
					continue;
				}
			}

			_lines.Add(line);
			added++;
			if (line.Timestamp.Length > 0)
			{
				int order = string.CompareOrdinal(line.Timestamp, Newest);
				if (order > 0)
				{
					Newest = line.Timestamp;
					_atNewest = 1;
				}
				else if (order == 0)
				{
					_atNewest++;
				}
			}
		}

		if (_lines.Count > MaxLines)
		{
			_lines.RemoveRange(0, _lines.Count - MaxLines);
		}

		if (added > 0)
		{
			Version++;
		}

		return added;
	}
}
