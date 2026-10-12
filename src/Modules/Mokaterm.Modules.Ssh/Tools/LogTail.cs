namespace Mokaterm.Modules.Ssh.Tools;

/// <summary>
/// The lines a log view holds, read once or followed. Following polls for what arrived since the newest timestamp
/// seen (<c>docker logs --since</c>), and such a since includes lines at that very instant, so the lines already here
/// at that timestamp are counted and skipped once rather than shown twice. A poll that continues from an exact place
/// (a journal cursor, a file offset) appends with <see cref="AppendAll"/> instead.
/// </summary>
/// <remarks>Public only because <see cref="Components.LogView"/> is a component and takes one.</remarks>
public sealed class LogTail
{
	/// <summary>The most lines a tail keeps; the oldest go first.</summary>
	public const int MaxLines = 10_000;

	private readonly List<LogLine> _lines = [];
	private int _atNewest;

	public IReadOnlyList<LogLine> Lines => _lines;

	/// <summary>The newest timestamp seen, for the next poll's since; empty until a line with one arrived.</summary>
	public string Newest { get; private set; } = "";

	/// <summary>Bumped by every change, so a view can tell whether to draw again.</summary>
	public int Version { get; private set; }

	/// <summary>Starts over with <paramref name="lines"/>, as a fresh read does.</summary>
	public void Reset(IReadOnlyList<LogLine> lines)
	{
		ArgumentNullException.ThrowIfNull(lines);
		_lines.Clear();
		Newest = "";
		_atNewest = 0;
		Version++;
		Add(lines, skipSeen: false);
	}

	/// <summary>Adds what a poll since <see cref="Newest"/> printed, leaving out what is already here.</summary>
	/// <returns>How many lines were new.</returns>
	public int Append(IReadOnlyList<LogLine> lines)
	{
		ArgumentNullException.ThrowIfNull(lines);
		return Add(lines, skipSeen: true);
	}

	/// <summary>
	/// Adds every line of a poll that picks up exactly where the last one stopped (a journal cursor, a byte offset into a
	/// file), so nothing it prints is a repeat, even a line at the same second as the newest one here.
	/// </summary>
	/// <returns>How many lines were added.</returns>
	public int AppendAll(IReadOnlyList<LogLine> lines)
	{
		ArgumentNullException.ThrowIfNull(lines);
		return Add(lines, skipSeen: false);
	}

	private int Add(IReadOnlyList<LogLine> lines, bool skipSeen)
	{
		int added = 0;
		int toSkip = skipSeen ? _atNewest : 0;
		foreach (LogLine line in lines)
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
