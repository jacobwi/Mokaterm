using System.Text;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Modules.Ssh.Admin;
using Mokaterm.Modules.Ssh.Admin.SystemLogs;
using Mokaterm.Modules.Ssh.Shell;
using Mokaterm.Modules.Ssh.Tools;

namespace Mokaterm.Modules.Ssh.Tests.Admin;

/// <summary>Following the journal by cursor and a file by byte offset, rotation included.</summary>
public sealed class LogFollowTests
{
	private const string Cursor = "s=739ad463348b4ceca5a9e69c95a3c93f;i=4ece7;b=6c7c6013a8cf4aa0b9a2d3b0ab9b2ec5;m=5c7d3eb4;t=5f8d1c6b0a8e6;x=d4b7a0d33b6b1d2b";

	private static readonly TimeSpan ShellTimeout = TimeSpan.FromSeconds(30);

	[Fact]
	public void Journal_TakesTheCursorOffTheLines()
	{
		string output = $"""
			@@mt:anchor
			-- cursor: s=aa;i=1
			@@mt:lines
			2026-10-11T08:00:03+00:00 web1 systemd[1]: Starting backup-agent.service...
			Hint: You are currently not seeing messages from other users and the system.
			2026-10-11T08:00:03+00:00 web1 backup-agent[48130]: error: destination not reachable
			-- cursor: {Cursor}

			""";

		JournalRead read = LogCommands.ParseJournal(output.ReplaceLineEndings("\n"));

		Assert.Equal(Cursor, read.Cursor);
		Assert.Equal(3, read.Lines.Count);
		Assert.Equal("2026-10-11T08:00:03+00:00", read.Lines[0].Timestamp);
		Assert.DoesNotContain(read.Lines, line => line.Text.Contains("cursor", StringComparison.Ordinal));
	}

	[Fact]
	public void Journal_WithNothingMatching_FollowsFromTheNewestEntry()
	{
		JournalRead read = LogCommands.ParseJournal("@@mt:anchor\n-- cursor: s=aa;i=9\n@@mt:lines\n");

		Assert.Empty(read.Lines);
		Assert.Equal("s=aa;i=9", read.Cursor);
		Assert.Null(LogCommands.ParseJournal("@@mt:anchor\n@@mt:lines\n").Cursor);
	}

	[Fact]
	public void Journal_Scripts_UseUtcAndTheCursor()
	{
		AdminScript read = LogCommands.Journal("nginx.service", "err", 200, asRoot: false);
		AdminScript follow = LogCommands.FollowJournal(Cursor, "nginx.service", "err", asRoot: true);

		Assert.Contains("journalctl --no-pager -o short-iso --utc --show-cursor -n 200 -u 'nginx.service' -p err 2>&1", read.Body, StringComparison.Ordinal);
		Assert.Equal($"journalctl --no-pager -q -o short-iso --utc --show-cursor --after-cursor '{Cursor}' -n 2000 -u 'nginx.service' -p err", follow.Body);
		Assert.True(follow.AsRoot);
		Assert.Throws<ArgumentException>(() => LogCommands.FollowJournal("s=1'; rm -rf /", null, null, asRoot: false));
	}

	[Theory]
	[InlineData("s=aa;i=1", true)]
	[InlineData(Cursor, true)]
	[InlineData("", false)]
	[InlineData("s=aa i=1", false)]
	[InlineData("s='x'", false)]
	public void Cursors(string cursor, bool valid) => Assert.Equal(valid, LogCommands.IsCursor(cursor));

	[Fact]
	public void JournalFollower_AppendsWhatCameAfter_AndKeepsItsPlaceWhenNothingDid()
	{
		LogFollower follower = LogFollower.ForJournal(new JournalRead([], "s=aa;i=1"), "nginx.service", null, asRoot: false)!;

		LogFollowStep step = follower.Take(new AdminOutcome { ExitCode = 0, Output = "2026-10-11T08:00:04+00:00 web1 nginx[1]: one\n-- cursor: s=aa;i=2\n" });
		Assert.Equal("web1 nginx[1]: one", Assert.Single(step.Lines).Text);
		Assert.Equal("s=aa;i=2", follower.Position);

		Assert.Empty(follower.Take(new AdminOutcome { ExitCode = 0, Output = "" }).Lines);
		Assert.Equal("s=aa;i=2", follower.Position);
		Assert.Contains("--after-cursor 's=aa;i=2'", follower.Next().Body, StringComparison.Ordinal);

		AdminException failed = Assert.Throws<AdminException>(() => follower.Take(new AdminOutcome { ExitCode = 1, Error = "Failed to seek to cursor: Invalid argument\n" }));
		Assert.Equal("Failed to seek to cursor: Invalid argument", failed.Message);
		Assert.Equal("s=aa;i=2", follower.Position);
		Assert.Null(LogFollower.ForJournal(new JournalRead([], null), null, null, asRoot: false));
	}

	[Fact]
	public void File_ReadsInodeSizeAndLines_EvenWhenALineLooksLikeAPart()
	{
		FileRead read = LogCommands.ParseFile("@@mt:file 131077 4096\n@@mt:lines\nfirst\n@@mt:rotated\nsecond\n");

		Assert.Equal("131077", read.Inode);
		Assert.Equal(4096, read.Size);
		Assert.False(read.Rotated);
		Assert.Equal("first\n@@mt:rotated\nsecond\n", read.Text);
		Assert.Null(LogCommands.ParseFile("@@mt:file  \n@@mt:lines\n").Size);
	}

	[Fact]
	public void FileFollower_HoldsBackAHalfLine_UntilItIsWhole()
	{
		(IReadOnlyList<LogLine> lines, LogFollower? follower) = LogFollower.ForFile("/var/log/syslog", new FileRead("12", 30, false, false, "one\ntwo\nthr"), asRoot: false);

		Assert.Equal(["one", "two"], lines.Select(line => line.Text));
		Assert.NotNull(follower);
		Assert.Equal("12@30", follower.Position);

		LogFollowStep step = follower.Take(Outcome("@@mt:file 12 42\n@@mt:lines\nee\nfour\n"));
		Assert.Equal(["three", "four"], step.Lines.Select(line => line.Text));
		Assert.Null(step.Notice);
		Assert.Equal("12@42", follower.Position);
		Assert.Contains("mt_from=42", follower.Next().Body, StringComparison.Ordinal);
		Assert.Contains("[ \"$1\" != '12' ]", follower.Next().Body, StringComparison.Ordinal);
	}

	[Fact]
	public void FileFollower_Rotation_StartsOverWithTheNewFile()
	{
		(_, LogFollower? follower) = LogFollower.ForFile("/var/log/syslog", new FileRead("12", 30, false, false, "one\nhalf"), asRoot: false);

		LogFollowStep step = follower!.Take(Outcome("@@mt:file 99 9\n@@mt:rotated\n@@mt:lines\nnew file\n"));

		Assert.Equal(["new file"], step.Lines.Select(line => line.Text));
		Assert.Contains("rotated", step.Notice, StringComparison.Ordinal);
		Assert.Equal("99@9", follower.Position);
	}

	[Fact]
	public void FileFollower_SkippedText_DropsItsFirstPartialLine()
	{
		(_, LogFollower? follower) = LogFollower.ForFile("/var/log/syslog", new FileRead("12", 30, false, false, "one\nhalf"), asRoot: false);

		LogFollowStep step = follower!.Take(Outcome("@@mt:file 12 900000\n@@mt:skipped\n@@mt:lines\nof a line\nwhole\n"));

		Assert.Equal(["whole"], step.Lines.Select(line => line.Text));
		Assert.Contains("skipped", step.Notice, StringComparison.Ordinal);
	}

	[Fact]
	public void FileFollower_AFileThatIsGone_WaitsWhereItWas()
	{
		(_, LogFollower? follower) = LogFollower.ForFile("/var/log/syslog", new FileRead("12", 30, false, false, "one\n"), asRoot: false);

		LogFollowStep step = follower!.Take(Outcome("@@mt:file  \n"));

		Assert.Empty(step.Lines);
		Assert.NotNull(step.Notice);
		Assert.Equal("12@30", follower.Position);
		Assert.Null(LogFollower.ForFile("/var/log/syslog", new FileRead("", null, false, false, ""), asRoot: false).Follower);
	}

	[Fact]
	public void FileFollower_ALineWithoutEnd_IsShownOnceItIsTooLongToHold()
	{
		(IReadOnlyList<LogLine> lines, _) = LogFollower.ForFile("/var/log/blob", new FileRead("1", 1, false, false, new string('x', LogFollower.MaxPendingChars + 1)), asRoot: false);

		Assert.Equal(LogFollower.MaxPendingChars + 1, Assert.Single(lines).Text.Length);
	}

	[Fact]
	public void Tail_AppendAll_KeepsLinesAtTheSameSecond()
	{
		LogTail tail = new();
		tail.Reset([new LogLine("2026-10-11T08:00:03+00:00", "a", false)]);

		int added = tail.AppendAll([new LogLine("2026-10-11T08:00:03+00:00", "b", false), new LogLine("2026-10-11T08:00:02+00:00", "c", false)]);

		Assert.Equal(2, added);
		Assert.Equal(["a", "b", "c"], tail.Lines.Select(line => line.Text));
	}

	[Theory]
	[InlineData("@@mt:lines\nx\n", "x\n")]
	[InlineData("@@mt:a\n1\n@@mt:lines\n@@mt:lines\ny", "@@mt:lines\ny")]
	[InlineData("@@mt:a\n@@mt:lines", "")]
	[InlineData("@@mt:a\n", null)]
	public void Rest_IsEverythingAfterTheFirstStart(string output, string? rest) => Assert.Equal(rest, AdminSections.Rest(output, "lines"));

	[Fact]
	public async Task FileScripts_RunInALocalShell_FollowGrowthAndRotation()
	{
		if (PosixShellLocator.Find() is not { } shell)
		{
			Assert.Skip("No POSIX sh is available on this machine.");
			return;
		}

		await using TempFolder folder = new();
		string file = Path.Combine(folder.Path, "app.log");
		await File.WriteAllTextAsync(file, "one\ntwo\nthree\n", TestContext.Current.CancellationToken);
		string unixFile = await UnixPathAsync(shell, file);

		(IReadOnlyList<LogLine> lines, LogFollower? follower) = LogFollower.ForFile("/var/log/app.log", LogCommands.ParseFile(await RunAsync(shell, LogCommands.ReadFile("/var/log/app.log", 2, asRoot: false), unixFile)), asRoot: false);
		Assert.Equal(["two", "three"], lines.Select(line => line.Text));
		Assert.NotNull(follower);

		await File.AppendAllTextAsync(file, "four\nfi", TestContext.Current.CancellationToken);
		LogFollowStep grown = follower.Take(new AdminOutcome { ExitCode = 0, Output = await RunAsync(shell, follower.Next(), unixFile) });
		Assert.Equal(["four"], grown.Lines.Select(line => line.Text));

		await File.AppendAllTextAsync(file, "ve\n", TestContext.Current.CancellationToken);
		LogFollowStep finished = follower.Take(new AdminOutcome { ExitCode = 0, Output = await RunAsync(shell, follower.Next(), unixFile) });
		Assert.Equal(["five"], finished.Lines.Select(line => line.Text));

		// logrotate's copytruncate: the same file, emptied and written again.
		await File.WriteAllTextAsync(file, "new\n", TestContext.Current.CancellationToken);
		LogFollowStep rotated = follower.Take(new AdminOutcome { ExitCode = 0, Output = await RunAsync(shell, follower.Next(), unixFile) });
		Assert.Equal(["new"], rotated.Lines.Select(line => line.Text));
		Assert.NotNull(rotated.Notice);
	}

	private static AdminOutcome Outcome(string output) => new() { ExitCode = 0, Output = output };

	/// <summary>Runs a log file script against a local file in place of the /var/log path it was built for.</summary>
	private static async Task<string> RunAsync(string shell, AdminScript script, string unixFile)
	{
		string text = AdminScriptText.Compose(script).Replace("mt_file='/var/log/app.log'", "mt_file=" + PosixShell.Quote(unixFile), StringComparison.Ordinal);
		(int exitCode, byte[] output, string error) = await PosixShellLocator.RunAsync(shell, PosixShellLocator.UnixPathPrefix + text, []).WaitAsync(ShellTimeout, TestContext.Current.CancellationToken);
		Assert.True(exitCode == 0, error);
		Assert.True(RemoteOutput.TryGetPayload(output, out ReadOnlyMemory<byte> payload));
		return Encoding.UTF8.GetString(payload.Span);
	}

	private static async Task<string> UnixPathAsync(string shell, string path)
	{
		if (!OperatingSystem.IsWindows())
		{
			return path;
		}

		(_, byte[] output, _) = await PosixShellLocator.RunAsync(shell, PosixShellLocator.UnixPathPrefix + "cygpath -u \"$1\"", [path]).WaitAsync(ShellTimeout, TestContext.Current.CancellationToken);
		return Encoding.UTF8.GetString(output).Trim();
	}
}
