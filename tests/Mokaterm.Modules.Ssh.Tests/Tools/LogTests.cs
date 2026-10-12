using Mokaterm.Modules.Ssh.Components;
using Mokaterm.Modules.Ssh.Tools;

namespace Mokaterm.Modules.Ssh.Tests.Tools;

/// <summary>The log types the session tools share: reading lines, following without repeats, and what the view draws.</summary>
public sealed class LogTests
{
	private const char Esc = (char)0x1B;

	[Fact]
	public void Read_ALineWithoutATimestamp_IsKeptWhole_AndEscapesAreDropped()
	{
		IReadOnlyList<LogLine> lines = [.. LogLines.Read($"plain text\r\n2026-10-11T07:00:01.5Z {Esc}[31mred{Esc}[0m\n", isError: false)];

		Assert.Equal(("", "plain text"), (lines[0].Timestamp, lines[0].Text));
		Assert.Equal(("2026-10-11T07:00:01.5Z", "red"), (lines[1].Timestamp, lines[1].Text));
	}

	[Fact]
	public void Read_TheJournal_TakesItsTimestampOff()
	{
		IReadOnlyList<LogLine> lines = [.. LogLines.Read("2026-10-11T08:00:03+0000 web1 backup-agent[48130]: error: unreachable\n-- No entries --\n", isError: false)];

		Assert.Equal("2026-10-11T08:00:03+0000", lines[0].Timestamp);
		Assert.Equal("web1 backup-agent[48130]: error: unreachable", lines[0].Text);
		Assert.Equal(("", "-- No entries --"), (lines[1].Timestamp, lines[1].Text));
	}

	[Fact]
	public void InOrder_IsStable_ForLinesAtTheSameInstantOrWithoutOne()
	{
		LogLine[] lines =
		[
			new("2026-10-11T07:00:02Z", "b", false),
			new("", "first unstamped", false),
			new("2026-10-11T07:00:01Z", "a", true),
			new("2026-10-11T07:00:02Z", "c", true),
			new("", "second unstamped", false),
		];

		Assert.Equal(["first unstamped", "second unstamped", "a", "b", "c"], LogLines.InOrder(lines).Select(line => line.Text));
	}

	[Theory]
	[InlineData("2026-10-11T07:00:00Z", true)]
	[InlineData("2026-10-11T07:00:00.123456789+02:00", true)]
	[InlineData("2026-10-11T07:00:00+0000", true)]
	[InlineData("2026-10-11 07:00:00", false)]
	[InlineData("yesterday", false)]
	public void IsTimestamp(string text, bool expected) => Assert.Equal(expected, LogLines.IsTimestamp(text));

	[Theory]
	[InlineData("2026-10-11T07:00:00.5Z", true)]
	[InlineData("2026-10-11T07:00:00Z rm", false)]
	[InlineData("2026-10-11T07:00:00Z\n", false)]
	[InlineData(null, false)]
	public void IsTimestampWord_RefusesWhatWouldSplitTheWord(string? text, bool expected) =>
		Assert.Equal(expected, LogLines.IsTimestampWord(text));

	[Fact]
	public void Tail_FollowingWithSince_ShowsEachLineOnce()
	{
		LogTail tail = new();
		tail.Reset(Stamped(("07:00:01", "a"), ("07:00:02", "b"), ("07:00:02", "c")));

		Assert.Equal("2026-10-11T07:00:02.000000000Z", tail.Newest);

		// A since is inclusive: the two lines at the newest instant come back, then a third at that instant and a newer one.
		int added = tail.Append(Stamped(("07:00:02", "b"), ("07:00:02", "c"), ("07:00:02", "d"), ("07:00:03", "e")));

		Assert.Equal(2, added);
		Assert.Equal(["a", "b", "c", "d", "e"], tail.Lines.Select(line => line.Text));
		Assert.Equal(0, tail.Append(Stamped(("07:00:03", "e"))));
	}

	[Fact]
	public void Tail_KeepsAtMostTheLimit_DroppingTheOldest()
	{
		LogTail tail = new();
		LogLine[] many = [.. Enumerable.Range(0, LogTail.MaxLines + 5).Select(index => new LogLine("", $"line {index}", false))];

		tail.Reset(many);

		Assert.Equal(LogTail.MaxLines, tail.Lines.Count);
		Assert.Equal("line 5", tail.Lines[0].Text);
	}

	[Fact]
	public void Tail_EveryResetIsANewVersion_EvenAnEmptyOne()
	{
		LogTail tail = new();
		int before = tail.Version;

		tail.Reset([]);
		int afterEmpty = tail.Version;
		tail.Append([]);

		Assert.NotEqual(before, afterEmpty);
		Assert.Equal(afterEmpty, tail.Version);
	}

	[Fact]
	public void View_Search_KeepsMatchingLines_AndMarksEachMatch()
	{
		LogLine[] lines = [new("", "GET /cart 200", false), new("", "POST /login 500", true), new("", "get /CART 304", false)];

		Assert.Equal(["GET /cart 200", "get /CART 304"], LogView.Matching(lines, "cart").Select(line => line.Text));
		Assert.Equal(3, LogView.Matching(lines, "").Count);
		Assert.Equal(
			[("get /", false), ("CART", true), (" 304", false)],
			LogView.Pieces("get /CART 304", "cart"));
		Assert.Equal([("a", true), ("a", true)], LogView.Pieces("aa", "a"));
	}

	[Theory]
	[InlineData("2026-10-11T07:00:01.123456789Z", "10-11 07:00:01.123")]
	[InlineData("2026-10-11T07:00:01+0000", "10-11 07:00:01")]
	[InlineData("short", "short")]
	public void View_ShortTime(string timestamp, string expected) => Assert.Equal(expected, LogView.ShortTime(timestamp));

	[Fact]
	public void View_Count_SaysWhatIsShown()
	{
		Assert.Equal("1 line", LogView.Describe(1, 1, searching: false, following: false));
		Assert.Equal("3 of 12 lines match, following", LogView.Describe(12, 3, searching: true, following: true));
	}

	private static LogLine[] Stamped(params (string Time, string Text)[] lines) =>
		[.. lines.Select(line => new LogLine($"2026-10-11T{line.Time}.000000000Z", line.Text, false))];
}
