using System.Text;
using Mokaterm.Modules.Ssh.Docker;

namespace Mokaterm.Modules.Ssh.Tests.Docker;

/// <summary>Log lines back in order, following without repeats, and text files that go back to the host unchanged.</summary>
public sealed class DockerLogsAndTextTests
{
	[Fact]
	public void Merge_PutsBothStreamsBackInOrder()
	{
		const string Output = "2026-10-11T07:00:01.000000000Z GET /\n2026-10-11T07:00:03.000000000Z GET /cart\n";
		const string Error = "2026-10-11T07:00:02.000000000Z warn: slow\n";

		IReadOnlyList<DockerLogLine> lines = DockerLogParser.Merge(Output, Error);

		Assert.Equal(["GET /", "warn: slow", "GET /cart"], lines.Select(line => line.Text));
		Assert.Equal([false, true, false], lines.Select(line => line.IsError));
		Assert.Equal("2026-10-11T07:00:01.000000000Z", lines[0].Timestamp);
	}

	[Fact]
	public void Lines_ALineWithoutATimestamp_IsKeptWhole_AndEscapesAreDropped()
	{
		IReadOnlyList<DockerLogLine> lines = [.. DockerLogParser.Lines("plain text\r\n2026-10-11T07:00:01.5Z \u001b[31mred\u001b[0m\n", isError: false)];

		Assert.Equal(("", "plain text"), (lines[0].Timestamp, lines[0].Text));
		Assert.Equal(("2026-10-11T07:00:01.5Z", "red"), (lines[1].Timestamp, lines[1].Text));
	}

	[Fact]
	public void MergeCompose_TakesTheContainerOffEachLine()
	{
		const string Output = """
			shop-web-1  | 2026-10-11T07:00:02.000000000Z GET / HTTP/1.1
			shop-db-1   | 2026-10-11T07:00:01.000000000Z LOG:  ready | accepting
			shop-web-1  | not stamped
			""";

		IReadOnlyList<DockerLogLine> lines = DockerLogParser.MergeCompose(Output, null);

		Assert.Equal(["shop-web-1", "shop-db-1", "shop-web-1"], lines.Select(line => line.Source));
		Assert.Equal(["not stamped", "LOG:  ready | accepting", "GET / HTTP/1.1"], lines.Select(line => line.Text));
	}

	[Fact]
	public void Tail_FollowingWithSince_ShowsEachLineOnce()
	{
		DockerLogTail tail = new();
		tail.Reset(DockerLogParser.Merge("2026-10-11T07:00:01.000000000Z a\n2026-10-11T07:00:02.000000000Z b\n2026-10-11T07:00:02.000000000Z c\n", null));

		Assert.Equal("2026-10-11T07:00:02.000000000Z", tail.Newest);

		// --since is inclusive: the two lines at the newest instant come back, then a third at that instant and a newer one.
		int added = tail.Append(DockerLogParser.Merge("2026-10-11T07:00:02.000000000Z b\n2026-10-11T07:00:02.000000000Z c\n2026-10-11T07:00:02.000000000Z d\n2026-10-11T07:00:03.000000000Z e\n", null));

		Assert.Equal(2, added);
		Assert.Equal(["a", "b", "c", "d", "e"], tail.Lines.Select(line => line.Text));
		Assert.Equal(0, tail.Append(DockerLogParser.Merge("2026-10-11T07:00:03.000000000Z e\n", null)));
	}

	[Fact]
	public void Tail_KeepsAtMostTheLimit_DroppingTheOldest()
	{
		DockerLogTail tail = new();
		DockerLogLine[] many = [.. Enumerable.Range(0, DockerLogTail.MaxLines + 5).Select(index => new DockerLogLine("", $"line {index}", false))];

		tail.Reset(many);

		Assert.Equal(DockerLogTail.MaxLines, tail.Lines.Count);
		Assert.Equal("line 5", tail.Lines[0].Text);
	}

	[Theory]
	[InlineData("2026-10-11T07:00:00Z", true)]
	[InlineData("2026-10-11T07:00:00.123456789+02:00", true)]
	[InlineData("2026-10-11 07:00:00", false)]
	[InlineData("yesterday", false)]
	public void IsTimestamp(string text, bool expected) => Assert.Equal(expected, DockerLogParser.IsTimestamp(text));

	[Fact]
	public void TextFile_RefusesWhatItCouldNotWriteBackUnchanged()
	{
		Assert.Null(HostTextFile.Decode([0x61, 0x00, 0x62], truncated: false, out _));
		Assert.Null(HostTextFile.Decode([0x61, 0xC3, 0x28], truncated: false, out _));
		Assert.Equal("é", HostTextFile.Decode([0xC3, 0xA9], truncated: false, out bool bom));
		Assert.False(bom);
	}

	[Fact]
	public void TextFile_ACutFile_LosesOnlyTheCharacterTheCutSplit()
	{
		Assert.Equal("a", HostTextFile.Decode([0x61, 0xC3], truncated: true, out _));
		Assert.Equal("a€", HostTextFile.Decode([0x61, 0xE2, 0x82, 0xAC, 0xF0, 0x9F, 0x98], truncated: true, out _));
		Assert.Null(HostTextFile.Decode([0x61, 0xC3], truncated: false, out _));

		// Being cut does not make a binary file text.
		Assert.Null(HostTextFile.Decode([0x61, 0xFF, 0x62, 0xC3], truncated: true, out _));
	}

	[Fact]
	public void TextFile_KeepsItsByteOrderMarkAndLineEndings()
	{
		byte[] original = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("A=1\r\nB=2\r\n")];
		string text = HostTextFile.Decode(original, truncated: false, out bool bom)!;
		HostTextFile file = new() { Path = "/srv/shop/.env", Text = text, HasByteOrderMark = bom, UsesCrLf = text.Contains("\r\n", StringComparison.Ordinal) };

		// The editor hands back line feeds, whatever the file used.
		byte[] written = file.Encode("A=1\nB=2\n");

		Assert.True(bom);
		Assert.Equal(original, written);
	}
}
