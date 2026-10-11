using System.Diagnostics;
using System.Globalization;
using Mokaterm.UI.FileBrowser.Editing;

namespace Mokaterm.UI.FileBrowser.Tests;

/// <summary>
/// The line diff behind the editor's review and its save conflict: the shortest set of changes, three lines of context
/// around each, runs of unchanged lines folded into one row, and a plain verdict instead of a frozen circuit when two
/// texts share too little to compare.
/// </summary>
public sealed class LineDiffTests
{
	[Fact]
	public void IdenticalTexts_HaveNoRows()
	{
		LineDiffResult diff = LineDiff.Compare("a\nb\nc\n", "a\nb\nc\n");

		Assert.False(diff.IsTooDifferent);
		Assert.False(diff.HasChanges);
		Assert.Empty(diff.Lines);
	}

	[Fact]
	public void TheFinalLineFeed_IsNotALineOfItsOwn()
	{
		LineDiffResult diff = LineDiff.Compare("a\nb", "a\nb\n");

		Assert.False(diff.HasChanges);
		Assert.Empty(diff.Lines);
	}

	[Fact]
	public void AnInsert_KeepsThreeLinesOfContextAndFoldsTheRest()
	{
		LineDiffResult diff = LineDiff.Compare(Numbered(1, 10), Insert(Numbered(1, 10), afterLine: 5, "new"));

		Assert.Equal(1, diff.Added);
		Assert.Equal(0, diff.Removed);
		Assert.Equal(
			[
				Skipped(1, 1, 2),
				Same(3, 3, "line 3"),
				Same(4, 4, "line 4"),
				Same(5, 5, "line 5"),
				Added(6, "new"),
				Same(6, 7, "line 6"),
				Same(7, 8, "line 7"),
				Same(8, 9, "line 8"),
				Skipped(9, 10, 2),
			],
			diff.Lines);
	}

	[Fact]
	public void ADelete_KeepsItsOldNumberAndNoNewOne()
	{
		LineDiffResult diff = LineDiff.Compare("a\nb\nc\nd\n", "a\nb\nd\n");

		Assert.Equal(0, diff.Added);
		Assert.Equal(1, diff.Removed);
		Assert.Equal([Same(1, 1, "a"), Same(2, 2, "b"), Removed(3, "c"), Same(4, 3, "d")], diff.Lines);
	}

	[Fact]
	public void AReplacedLine_ShowsTheRemovalBeforeTheAddition()
	{
		LineDiffResult diff = LineDiff.Compare("a\nb\nc", "a\nB\nc");

		Assert.Equal([Same(1, 1, "a"), Removed(2, "b"), Added(2, "B"), Same(3, 3, "c")], diff.Lines);
	}

	[Fact]
	public void AnEmptyOldText_IsAllAdded()
	{
		LineDiffResult diff = LineDiff.Compare("", "x\ny\n");

		Assert.Equal(2, diff.Added);
		Assert.Equal([Added(1, "x"), Added(2, "y")], diff.Lines);
	}

	[Fact]
	public void AnEmptyNewText_IsAllRemoved()
	{
		LineDiffResult diff = LineDiff.Compare("x\ny", "");

		Assert.Equal(2, diff.Removed);
		Assert.Equal([Removed(1, "x"), Removed(2, "y")], diff.Lines);
	}

	[Fact]
	public void EmptyLines_AreLinesLikeAnyOther()
	{
		LineDiffResult diff = LineDiff.Compare("a\n\nb\n", "a\nb\n");

		Assert.Equal([Same(1, 1, "a"), Removed(2, ""), Same(3, 2, "b")], diff.Lines);
	}

	[Fact]
	public void ChangesSixLinesApart_ShareTheirContext()
	{
		string after = Replace(Replace(Numbered(1, 20), 5, "five"), 12, "twelve");

		LineDiffResult diff = LineDiff.Compare(Numbered(1, 20), after);

		// Lines 6 to 11 are three lines after the first change and three before the second: nothing to fold between.
		Assert.Equal(
			[
				Skipped(1, 1, 1),
				Same(2, 2, "line 2"),
				Same(3, 3, "line 3"),
				Same(4, 4, "line 4"),
				Removed(5, "line 5"),
				Added(5, "five"),
				Same(6, 6, "line 6"),
				Same(7, 7, "line 7"),
				Same(8, 8, "line 8"),
				Same(9, 9, "line 9"),
				Same(10, 10, "line 10"),
				Same(11, 11, "line 11"),
				Removed(12, "line 12"),
				Added(12, "twelve"),
				Same(13, 13, "line 13"),
				Same(14, 14, "line 14"),
				Same(15, 15, "line 15"),
				Skipped(16, 16, 5),
			],
			diff.Lines);
	}

	[Fact]
	public void ChangesSevenLinesApart_AreSplitByAFoldedLine()
	{
		string after = Replace(Replace(Numbered(1, 20), 5, "five"), 13, "thirteen");

		LineDiffResult diff = LineDiff.Compare(Numbered(1, 20), after);

		DiffLine[] between = [.. diff.Lines.SkipWhile(line => line.Kind != DiffLineKind.Added).Skip(1).TakeWhile(line => line.Kind != DiffLineKind.Removed)];
		Assert.Equal(
			[
				Same(6, 6, "line 6"),
				Same(7, 7, "line 7"),
				Same(8, 8, "line 8"),
				Skipped(9, 9, 1),
				Same(10, 10, "line 10"),
				Same(11, 11, "line 11"),
				Same(12, 12, "line 12"),
			],
			between);
	}

	[Fact]
	public void MoreEditsThanTheCap_GiveUpRatherThanSearch()
	{
		LineDiffResult diff = LineDiff.Compare("a\nb\nc\nd\ne\nf\n", "1\n2\n3\n4\n5\n6\n", LineDiff.ContextLines, maxEdits: 4, maxWork: long.MaxValue);

		Assert.True(diff.IsTooDifferent);
		Assert.Empty(diff.Lines);
	}

	[Fact]
	public void MoreWorkThanTheCap_GivesUpRatherThanSearch()
	{
		string before = string.Concat(Enumerable.Range(0, 200).Select(index => $"{index % 7}\n"));
		string after = string.Concat(Enumerable.Range(0, 200).Select(index => $"{index % 5}\n"));

		Assert.True(LineDiff.Compare(before, after, LineDiff.ContextLines, LineDiff.MaxEdits, maxWork: 1000).IsTooDifferent);
		Assert.False(LineDiff.Compare(before, after).IsTooDifferent);
	}

	[Fact]
	public void ALongFileWithScatteredEdits_IsCompared()
	{
		string before = Numbered(1, 20_000);
		string after = before;
		for (int line = 100; line <= 20_000; line += 400)
		{
			after = Replace(after, line, "changed");
		}

		LineDiffResult diff = LineDiff.Compare(before, after);

		Assert.False(diff.IsTooDifferent);
		Assert.Equal(50, diff.Added);
		Assert.Equal(50, diff.Removed);
	}

	[Fact]
	public void TwoLongUnrelatedFiles_AreGivenUpOnQuickly()
	{
		string before = string.Concat(Enumerable.Range(0, 20_000).Select(index => $"old {index}\n"));
		string after = string.Concat(Enumerable.Range(0, 20_000).Select(index => $"new {index}\n"));

		Stopwatch clock = Stopwatch.StartNew();
		LineDiffResult diff = LineDiff.Compare(before, after);

		Assert.True(diff.IsTooDifferent);
		Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"Took {clock.Elapsed}.");
	}

	[Fact]
	public void RandomTexts_GetTheShortestEdit_AndTheRowsRebuildBothSides()
	{
		Random random = new(20261011);
		for (int round = 0; round < 2000; round++)
		{
			string[] before = RandomLines(random);
			string[] after = RandomLines(random);

			// Context wide enough that nothing folds, so the rows hold every line of both sides.
			LineDiffResult diff = LineDiff.Compare(Text(before), Text(after), 1_000_000, 10_000, long.MaxValue);

			Assert.False(diff.IsTooDifferent);
			if (!diff.HasChanges)
			{
				// Nothing changed, so there is nothing to show at all.
				Assert.Equal(before, after);
				Assert.Empty(diff.Lines);
				continue;
			}

			Assert.Equal(before, diff.Lines.Where(line => line.Kind != DiffLineKind.Added).Select(line => line.Text));
			Assert.Equal(after, diff.Lines.Where(line => line.Kind != DiffLineKind.Removed).Select(line => line.Text));
			Assert.Equal(Enumerable.Range(1, before.Length), diff.Lines.Where(line => line.Kind != DiffLineKind.Added).Select(line => line.OldNumber));
			Assert.Equal(Enumerable.Range(1, after.Length), diff.Lines.Where(line => line.Kind != DiffLineKind.Removed).Select(line => line.NewNumber));
			Assert.Equal(before.Length + after.Length - (2 * LongestCommon(before, after)), diff.Added + diff.Removed);
		}
	}

	private static string[] RandomLines(Random random)
	{
		string[] lines = new string[random.Next(0, 14)];
		for (int index = 0; index < lines.Length; index++)
		{
			lines[index] = random.Next(0, 4) switch
			{
				0 => "",
				1 => "a",
				2 => "b",
				_ => "c",
			};
		}

		return lines;
	}

	private static int LongestCommon(string[] before, string[] after)
	{
		int[,] table = new int[before.Length + 1, after.Length + 1];
		for (int i = before.Length - 1; i >= 0; i--)
		{
			for (int j = after.Length - 1; j >= 0; j--)
			{
				table[i, j] = string.Equals(before[i], after[j], StringComparison.Ordinal)
					? table[i + 1, j + 1] + 1
					: Math.Max(table[i + 1, j], table[i, j + 1]);
			}
		}

		return table[0, 0];
	}

	private static string Text(string[] lines) => string.Concat(lines.Select(line => line + "\n"));

	private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

	private static string Numbered(int first, int last) =>
		string.Concat(Enumerable.Range(first, last - first + 1).Select(line => $"line {Number(line)}\n"));

	private static string Insert(string text, int afterLine, string line)
	{
		List<string> lines = [.. LineDiff.SplitLines(text)];
		lines.Insert(afterLine, line);
		return Text([.. lines]);
	}

	private static string Replace(string text, int lineNumber, string line)
	{
		string[] lines = LineDiff.SplitLines(text);
		lines[lineNumber - 1] = line;
		return Text(lines);
	}

	private static DiffLine Same(int oldNumber, int newNumber, string text) => new(DiffLineKind.Unchanged, oldNumber, newNumber, text);

	private static DiffLine Added(int newNumber, string text) => new(DiffLineKind.Added, 0, newNumber, text);

	private static DiffLine Removed(int oldNumber, string text) => new(DiffLineKind.Removed, oldNumber, 0, text);

	private static DiffLine Skipped(int oldNumber, int newNumber, int count) => new(DiffLineKind.Skipped, oldNumber, newNumber, "", count);
}
