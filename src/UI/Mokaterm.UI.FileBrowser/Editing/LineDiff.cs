namespace Mokaterm.UI.FileBrowser.Editing;

/// <summary>
/// Compares two texts line by line with Myers' O(ND) algorithm, the one diff and git use, and keeps
/// <see cref="ContextLines"/> unchanged lines around every change. The texts are the editor's, already in line feeds, so
/// lines compare ordinally. A final line feed ends the last line instead of opening an empty one after it, the way a
/// file reads on disk: whether a file ends with one is decided when it is written, not by an edit to any line.
/// </summary>
internal static class LineDiff
{
	public const int ContextLines = 3;

	/// <summary>
	/// The most lines the two sides may differ by. The search keeps a row of positions for every step it takes, about
	/// this squared over two integers in all, and a review past this many changed lines says no more than "most of it".
	/// </summary>
	public const int MaxEdits = 2000;

	/// <summary>
	/// The most line comparisons the search makes. Two long files that share little can stay under
	/// <see cref="MaxEdits"/> and still cost their length times the edits, which would hold the circuit for seconds.
	/// </summary>
	public const long MaxWork = 20_000_000;

	public static LineDiffResult Compare(string before, string after) =>
		Compare(before, after, ContextLines, MaxEdits, MaxWork);

	internal static LineDiffResult Compare(string before, string after, int context, int maxEdits, long maxWork)
	{
		string[] oldLines = SplitLines(before);
		string[] newLines = SplitLines(after);

		// An edit usually touches a few lines of a long file, so the shared head and tail are matched before the search
		// starts, which leaves its budget to the part that changed.
		int shorter = Math.Min(oldLines.Length, newLines.Length);
		int head = 0;
		while (head < shorter && string.Equals(oldLines[head], newLines[head], StringComparison.Ordinal))
		{
			head++;
		}

		int tail = 0;
		while (tail < shorter - head
			&& string.Equals(oldLines[^(tail + 1)], newLines[^(tail + 1)], StringComparison.Ordinal))
		{
			tail++;
		}

		List<(int Old, int New, int Length)> matches = [];
		if (head > 0)
		{
			matches.Add((0, 0, head));
		}

		if (!Search(oldLines, head, oldLines.Length - tail, newLines, head, newLines.Length - tail, matches, maxEdits, maxWork))
		{
			return LineDiffResult.TooDifferent;
		}

		if (tail > 0)
		{
			matches.Add((oldLines.Length - tail, newLines.Length - tail, tail));
		}

		return Render(oldLines, newLines, Merge(matches), context);
	}

	/// <summary>The lines a text area shows, less the empty one after a final line feed.</summary>
	internal static string[] SplitLines(string text)
	{
		if (text.Length == 0)
		{
			return [];
		}

		string[] lines = text.Split('\n');
		return text[^1] == '\n' ? lines[..^1] : lines;
	}

	/// <summary>
	/// Finds the shortest edit between the two ranges and appends the runs of lines both sides keep, in order. Returns
	/// false when that edit is longer than <paramref name="maxEdits"/> lines or finding it takes more than
	/// <paramref name="maxWork"/> steps.
	/// </summary>
	private static bool Search(
		string[] oldLines,
		int oldFrom,
		int oldTo,
		string[] newLines,
		int newFrom,
		int newTo,
		List<(int Old, int New, int Length)> matches,
		int maxEdits,
		long maxWork)
	{
		int oldCount = oldTo - oldFrom;
		int newCount = newTo - newFrom;
		if (oldCount == 0 || newCount == 0)
		{
			// Only added or only removed lines, and nothing in common to look for.
			return true;
		}

		// Numbers rather than strings, so every step of the search compares two integers.
		Dictionary<string, int> ids = new(StringComparer.Ordinal);
		int[] before = Intern(oldLines, oldFrom, oldCount, ids);
		int[] after = Intern(newLines, newFrom, newCount, ids);

		int limit = Math.Min(oldCount + newCount, maxEdits);
		int offset = limit + 1;

		// reach[offset + k] is how far along the old side the best path on diagonal k (old index minus new index) has got.
		int[] reach = new int[(2 * limit) + 3];
		List<int[]> trace = [];
		long work = 0;
		for (int edits = 0; edits <= limit; edits++)
		{
			for (int k = -edits; k <= edits; k += 2)
			{
				int x = k == -edits || (k != edits && reach[offset + k - 1] < reach[offset + k + 1])
					? reach[offset + k + 1]
					: reach[offset + k - 1] + 1;
				int y = x - k;
				int runStart = x;
				while (x < oldCount && y < newCount && before[x] == after[y])
				{
					x++;
					y++;
				}

				work += x - runStart + 1;
				reach[offset + k] = x;
				if (x >= oldCount && y >= newCount)
				{
					Backtrack(trace, edits, oldCount, newCount, oldFrom, newFrom, matches);
					return true;
				}
			}

			if (work > maxWork)
			{
				return false;
			}

			// Only the diagonals this round could reach, which is what walking back needs: one int per diagonal.
			int[] row = new int[edits + 1];
			for (int index = 0; index <= edits; index++)
			{
				row[index] = reach[offset - edits + (2 * index)];
			}

			trace.Add(row);
		}

		return false;
	}

	/// <summary>Walks the shortest path back from the end and appends the diagonal runs on it, which are the shared lines.</summary>
	private static void Backtrack(
		List<int[]> trace,
		int edits,
		int oldCount,
		int newCount,
		int oldFrom,
		int newFrom,
		List<(int Old, int New, int Length)> matches)
	{
		List<(int Old, int New, int Length)> found = [];
		int x = oldCount;
		int y = newCount;
		for (int step = edits; step > 0; step--)
		{
			int[] row = trace[step - 1];
			int k = x - y;
			bool down = k == -step || (k != step && At(row, step - 1, k - 1) < At(row, step - 1, k + 1));
			int previousK = down ? k + 1 : k - 1;
			int previousX = At(row, step - 1, previousK);

			// A step down adds a line and keeps x; a step right removes one. The run after it ends where this round did.
			int runStart = down ? previousX : previousX + 1;
			if (x > runStart)
			{
				found.Add((oldFrom + runStart, newFrom + runStart - k, x - runStart));
			}

			x = previousX;
			y = previousX - previousK;
		}

		if (x > 0)
		{
			found.Add((oldFrom, newFrom, x));
		}

		found.Reverse();
		matches.AddRange(found);
	}

	private static int At(int[] row, int edits, int k) => row[(k + edits) / 2];

	private static int[] Intern(string[] lines, int from, int count, Dictionary<string, int> ids)
	{
		int[] result = new int[count];
		for (int index = 0; index < count; index++)
		{
			string line = lines[from + index];
			if (!ids.TryGetValue(line, out int id))
			{
				id = ids.Count;
				ids.Add(line, id);
			}

			result[index] = id;
		}

		return result;
	}

	/// <summary>Joins runs that touch, so every run left is followed by a change or by the end of both texts.</summary>
	private static List<(int Old, int New, int Length)> Merge(List<(int Old, int New, int Length)> matches)
	{
		List<(int Old, int New, int Length)> merged = [];
		foreach ((int Old, int New, int Length) match in matches)
		{
			if (match.Length == 0)
			{
				continue;
			}

			if (merged.Count > 0)
			{
				(int Old, int New, int Length) last = merged[^1];
				if (last.Old + last.Length == match.Old && last.New + last.Length == match.New)
				{
					merged[^1] = (last.Old, last.New, last.Length + match.Length);
					continue;
				}
			}

			merged.Add(match);
		}

		return merged;
	}

	private static LineDiffResult Render(string[] oldLines, string[] newLines, List<(int Old, int New, int Length)> matches, int context)
	{
		List<DiffLine> lines = [];
		int added = 0;
		int removed = 0;
		int oldAt = 0;
		int newAt = 0;

		// One pass past the last run, so the change after it (if any) is written as well.
		for (int index = 0; index <= matches.Count; index++)
		{
			(int matchOld, int matchNew, int length) = index < matches.Count ? matches[index] : (oldLines.Length, newLines.Length, 0);

			// Everything one side has before the next shared run is the change, removals first as diff prints them.
			for (int line = oldAt; line < matchOld; line++)
			{
				lines.Add(new DiffLine(DiffLineKind.Removed, line + 1, 0, oldLines[line]));
				removed++;
			}

			for (int line = newAt; line < matchNew; line++)
			{
				lines.Add(new DiffLine(DiffLineKind.Added, 0, line + 1, newLines[line]));
				added++;
			}

			if (length > 0)
			{
				bool changeBefore = matchOld > 0 || matchNew > 0;
				bool changeAfter = matchOld + length < oldLines.Length || matchNew + length < newLines.Length;
				AddUnchanged(lines, oldLines, matchOld, matchNew, length, changeBefore ? context : 0, changeAfter ? context : 0);
			}

			oldAt = matchOld + length;
			newAt = matchNew + length;
		}

		// With no change at all the one run has nothing on either side and came out as a single skipped row.
		return added == 0 && removed == 0 ? LineDiffResult.Of([], 0, 0) : LineDiffResult.Of(lines, added, removed);
	}

	/// <summary>Keeps <paramref name="lead"/> lines after the change above and <paramref name="trail"/> before the one below.</summary>
	private static void AddUnchanged(List<DiffLine> lines, string[] oldLines, int oldStart, int newStart, int length, int lead, int trail)
	{
		if (length <= lead + trail)
		{
			AddRun(lines, oldLines, oldStart, newStart, length);
			return;
		}

		AddRun(lines, oldLines, oldStart, newStart, lead);
		int skipped = length - lead - trail;
		lines.Add(new DiffLine(DiffLineKind.Skipped, oldStart + lead + 1, newStart + lead + 1, "", skipped));
		AddRun(lines, oldLines, oldStart + lead + skipped, newStart + lead + skipped, trail);
	}

	private static void AddRun(List<DiffLine> lines, string[] oldLines, int oldStart, int newStart, int count)
	{
		for (int offset = 0; offset < count; offset++)
		{
			lines.Add(new DiffLine(DiffLineKind.Unchanged, oldStart + offset + 1, newStart + offset + 1, oldLines[oldStart + offset]));
		}
	}
}
