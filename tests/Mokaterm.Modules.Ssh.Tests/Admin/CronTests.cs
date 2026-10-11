using Mokaterm.Modules.Ssh.Admin;
using Mokaterm.Modules.Ssh.Admin.Cron;

namespace Mokaterm.Modules.Ssh.Tests.Admin;

public sealed class CronTests
{
	[Theory]
	[InlineData("0 3 * * *", "Every day at 03:00")]
	[InlineData("30 23 * * *", "Every day at 23:30")]
	[InlineData("* * * * *", "Every minute")]
	[InlineData("*/5 * * * *", "Every 5 minutes")]
	[InlineData("15 * * * *", "Every hour at minute 15")]
	[InlineData("0 * * * *", "Every hour, on the hour")]
	[InlineData("0 */2 * * *", "Every 2 hours, on the hour")]
	[InlineData("10 */6 * * *", "Every 6 hours at minute 10")]
	[InlineData("0 9-17 * * *", "Every hour from 09:00 to 17:00")]
	[InlineData("0 6,18 * * *", "Every day at 06:00 and 18:00")]
	[InlineData("0,30 3 * * *", "Every day at 03:00 and 03:30")]
	[InlineData("*/10 9-17 * * 1-5", "Every 10 minutes from 09:00 to 17:59, on Monday to Friday")]
	[InlineData("* 3 * * *", "Every minute from 03:00 to 03:59")]
	[InlineData("0 8 * * 1-5", "Monday to Friday at 08:00")]
	[InlineData("0 4 * * 0", "Every Sunday at 04:00")]
	[InlineData("0 4 * * 7", "Every Sunday at 04:00")]
	[InlineData("0 4 * * sun", "Every Sunday at 04:00")]
	[InlineData("0 4 * * 1,3,5", "Monday, Wednesday and Friday at 04:00")]
	[InlineData("0 10 * * 6,0", "Saturday and Sunday at 10:00")]
	[InlineData("0 2 1 * *", "Every month on day 1 at 02:00")]
	[InlineData("0 0 1 1 *", "Every year on 1 January at 00:00")]
	[InlineData("0 0 1,15 * *", "At 00:00, on days 1 and 15 of the month")]
	[InlineData("0 0 1-7 * *", "At 00:00, on days 1 to 7 of the month")]
	[InlineData("0 0 */2 * *", "At 00:00, on every 2nd day of the month")]
	[InlineData("0 0 1 * 1", "At 00:00, on day 1 of the month or on Monday")]
	[InlineData("0 0 */2 * 1", "At 00:00, on every 2nd day of the month, when it falls on Monday")]
	[InlineData("0 12 * 6-8 *", "At 12:00, from June to August")]
	[InlineData("0 12 * jan,jul *", "At 12:00, in January and July")]
	[InlineData("*/15 * * * 1-5", "Every 15 minutes, on Monday to Friday")]
	[InlineData("5,10,15,20 * * * *", "Every hour at minutes 5, 10, 15 and 20")]
	public void Describe_ReadsLikeASentence(string schedule, string expected)
	{
		CronSchedule parsed = CronSchedule.TryParse(schedule, out string? problem) ?? throw new InvalidOperationException(problem);

		Assert.Equal(expected, parsed.Describe());
	}

	[Theory]
	[InlineData("0 3 * * *", "2026-10-12 03:00", "2026-10-13 03:00", "2026-10-14 03:00")]
	[InlineData("*/15 * * * *", "2026-10-11 09:45", "2026-10-11 10:00", "2026-10-11 10:15")]
	[InlineData("41 9 * * *", "2026-10-12 09:41", "2026-10-13 09:41", "2026-10-14 09:41")]
	[InlineData("0 0 1 * 1", "2026-10-12 00:00", "2026-10-19 00:00", "2026-10-26 00:00", "2026-11-01 00:00")]
	[InlineData("0 0 */2 * 1", "2026-10-19 00:00", "2026-11-09 00:00")]
	[InlineData("0 12 29 2 *", "2028-02-29 12:00")]
	public void NextRuns_FollowCronsDayRule(string schedule, params string[] expected)
	{
		CronSchedule parsed = CronSchedule.TryParse(schedule, out string? problem) ?? throw new InvalidOperationException(problem);
		DateTime now = new(2026, 10, 11, 9, 41, 30, DateTimeKind.Unspecified);

		IReadOnlyList<DateTime> runs = parsed.NextRuns(now, expected.Length);

		Assert.Equal(expected, runs.Select(run => run.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture)));
	}

	[Theory]
	[InlineData("@reboot")]
	[InlineData("0 0 30 2 *")]
	public void NextRuns_NoneForBootOrAnImpossibleDay(string schedule) =>
		Assert.Empty(CronSchedule.TryParse(schedule, out _)!.NextRuns(new DateTime(2026, 10, 11, 9, 41, 0, DateTimeKind.Unspecified), 3));

	[Theory]
	[InlineData("@reboot", "At every boot")]
	[InlineData("@daily", "Every day at 00:00")]
	[InlineData("@midnight", "Every day at 00:00")]
	[InlineData("@hourly", "Every hour, on the hour")]
	[InlineData("@weekly", "Every Sunday at 00:00")]
	[InlineData("@monthly", "Every month on day 1 at 00:00")]
	[InlineData("@yearly", "Every year on 1 January at 00:00")]
	[InlineData("@ANNUALLY", "Every year on 1 January at 00:00")]
	public void Describe_Nicknames(string schedule, string expected) =>
		Assert.Equal(expected, CronSchedule.TryParse(schedule, out _)!.Describe());

	[Theory]
	[InlineData("60 * * * *", "minute")]
	[InlineData("* 24 * * *", "hour")]
	[InlineData("* * 0 * *", "day of the month")]
	[InlineData("* * 32 * *", "day of the month")]
	[InlineData("* * * 13 *", "month")]
	[InlineData("* * * foo *", "month")]
	[InlineData("* * * * 8", "day of the week")]
	[InlineData("* * * * mon-xyz", "day of the week")]
	[InlineData("5-1 * * * *", "backwards")]
	[InlineData("*/0 * * * *", "step")]
	[InlineData("*/61 * * * *", "step")]
	[InlineData("5/10 * * * *", "single value")]
	[InlineData("* * * *", "five fields")]
	[InlineData("* * * * * *", "five fields")]
	[InlineData("@sometimes", "not a schedule")]
	[InlineData("1,,2 * * * *", "minute")]
	[InlineData("? * * * *", "minute")]
	public void Validation_SaysWhatIsWrong(string schedule, string fragment)
	{
		Assert.Null(CronSchedule.TryParse(schedule, out string? problem));
		Assert.Contains(fragment, problem, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void Fields_MatchTheValuesCronWould()
	{
		CronSchedule schedule = CronSchedule.TryParse("0-10/5,30 */6 1-31/10 jan-mar 7", out _)!;

		Assert.Equal([0, 5, 10, 30], schedule.Minute.Values);
		Assert.Equal([0, 6, 12, 18], schedule.Hour.Values);
		Assert.Equal([1, 11, 21, 31], schedule.DayOfMonth.Values);
		Assert.Equal([1, 2, 3], schedule.Month.Values);
		Assert.Equal([0], schedule.DayOfWeek.Values);
		Assert.Equal(6, schedule.Hour.EveryStep);
		Assert.True(CronSchedule.TryParse("* * * * 0-7", out _)!.DayOfWeek.IsAll);
	}

	[Fact]
	public void Document_KeepsEveryLineItDidNotTouch()
	{
		const string Text = """
			# m h dom mon dow command
			MAILTO=ops@example.com
			PATH = /usr/local/bin:/usr/bin:/bin

			0 3 * * * /usr/local/bin/backup.sh  --full   >/dev/null 2>&1
			@reboot   /usr/local/bin/start-agent
			61 * * * * broken
			*/5 * * * * date +\%F >> /tmp/stamp
			""";

		CrontabDocument document = CrontabDocument.Parse(Text);

		Assert.Equal(8, document.Lines.Count);
		Assert.Equal(
			[CrontabLineKind.Comment, CrontabLineKind.Variable, CrontabLineKind.Variable, CrontabLineKind.Blank, CrontabLineKind.Entry, CrontabLineKind.Entry, CrontabLineKind.Invalid, CrontabLineKind.Entry],
			document.Lines.Select(line => line.Kind));
		Assert.Equal(Text.ReplaceLineEndings("\n") + "\n", document.ToText());

		(int index, CrontabEntry backup) = document.Entries.First();
		Assert.Equal(4, index);
		Assert.Equal("0 3 * * *", backup.Schedule.Text);
		Assert.Equal("/usr/local/bin/backup.sh  --full   >/dev/null 2>&1", backup.Command);
		Assert.Equal("@reboot", document.Entries.ElementAt(1).Entry.Schedule.Text);
		Assert.Equal(7, Assert.Single(document.Problems).Number);

		CrontabEntry changed = CrontabEntry.TryCreate("30 4 * * 1-5", "/usr/local/bin/backup.sh --incremental", out _)!;
		CrontabDocument edited = document.Replace(index, changed);
		string[] lines = edited.ToText().Split('\n');
		Assert.Equal("30 4 * * 1-5 /usr/local/bin/backup.sh --incremental", lines[4]);
		Assert.Equal("61 * * * * broken", lines[6]);
		Assert.Equal("MAILTO=ops@example.com", lines[1]);

		CrontabDocument removed = edited.Remove(index);
		Assert.Equal(7, removed.Lines.Count);
		Assert.DoesNotContain("backup.sh", removed.ToText(), StringComparison.Ordinal);

		CrontabDocument added = CrontabDocument.Parse("").Add(changed);
		Assert.Equal("30 4 * * 1-5 /usr/local/bin/backup.sh --incremental\n", added.ToText());
		Assert.Equal("", CrontabDocument.Parse("").ToText());
	}

	[Fact]
	public void SystemFiles_NameTheUser()
	{
		CrontabDocument document = CrontabDocument.Parse("SHELL=/bin/sh\n17 * * * * root cd / && run-parts --report /etc/cron.hourly\n@reboot www-data /srv/start\n* * * * *\n", hasUserField: true);

		CrontabEntry hourly = document.Entries.First().Entry;
		Assert.Equal("root", hourly.User);
		Assert.Equal("cd / && run-parts --report /etc/cron.hourly", hourly.Command);
		Assert.Equal("17 * * * * root cd / && run-parts --report /etc/cron.hourly", hourly.Format());
		Assert.Equal("www-data", document.Entries.Last().Entry.User);
		Assert.Single(document.Problems);
	}

	[Theory]
	[InlineData("", "Type the command")]
	[InlineData("   ", "Type the command")]
	[InlineData("echo a\necho b", "one line")]
	[InlineData("echo \u0001", "one line")]
	public void Commands_AreOneLine(string command, string fragment) =>
		Assert.Contains(fragment, CrontabEntry.CheckCommand(command), StringComparison.Ordinal);

	[Theory]
	[InlineData("date +%F", true)]
	[InlineData("date +\\%F", false)]
	[InlineData("backup.sh", false)]
	public void PercentSigns_GetANote(string command, bool noted) =>
		Assert.Equal(noted, CrontabEntry.PercentNote(command) is not null);

	[Fact]
	public void Read_TreatsNoCrontabAsEmpty()
	{
		Assert.Equal("", CronCommands.ReadResult(new AdminOutcome { ExitCode = 1, Error = "no crontab for alice\n" }, out _));
		Assert.Equal("", CronCommands.ReadResult(new AdminOutcome { ExitCode = 1, Error = "crontab: can't open 'al': No such file or directory\n" }, out _));
		Assert.Equal("0 3 * * * x\n", CronCommands.ReadResult(new AdminOutcome { ExitCode = 0, Output = "0 3 * * * x\n" }, out _));
		Assert.Null(CronCommands.ReadResult(new AdminOutcome { ExitCode = 1, Error = "crontab: your UID isn't in the passwd file. bailing out.\n" }, out string? problem));
		Assert.Contains("bailing out", problem, StringComparison.Ordinal);
	}

	[Fact]
	public void Write_SendsTheTextOnStdin_AndGuardsAgainstAChangeInBetween()
	{
		CrontabDocument document = CrontabDocument.Parse("0 3 * * * a\n");
		(AdminAction action, string input) = CronCommands.Write(document, "0 4 * * * old\n", asRoot: false, "alice", "Change it");

		Assert.Equal("0 3 * * * a\n", input);
		Assert.True(action.Script.ReadsInput);
		Assert.False(action.Script.AsRoot);
		Assert.EndsWith("crontab -", action.Script.Body, StringComparison.Ordinal);
		Assert.Contains($"'{CronCommands.Sha256("0 4 * * * old\n")}'", action.Script.Body, StringComparison.Ordinal);
		Assert.DoesNotContain("0 3 * * * a", action.Script.Body, StringComparison.Ordinal);
		Assert.Null(action.Warning);

		Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", CronCommands.Sha256(""));
		Assert.NotNull(CronCommands.Write(CrontabDocument.Parse("nonsense\n"), "", true, "root", "x").Action.Warning);
	}
}
