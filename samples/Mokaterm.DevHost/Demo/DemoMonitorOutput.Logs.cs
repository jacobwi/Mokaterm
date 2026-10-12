using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Mokaterm.DevHost.Demo;

internal static partial class DemoMonitorOutput
{
	/// <summary>Seconds between two journal entries the demo makes up as time passes.</summary>
	private const int JournalEvery = 4;

	/// <summary>Seconds between two lines of a demo log file.</summary>
	private const int FileEvery = 3;

	/// <summary>Every demo log line is padded to this many bytes, so a byte offset is a line number times it.</summary>
	private const int FileLineBytes = 112;

	/// <summary>Where the made-up journal entries start, a while before the demo machine's oldest canned one.</summary>
	private static readonly DateTimeOffset JournalStart = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

	private static readonly string[] JournalMessages =
	[
		"web1 nginx[1124]: 203.0.113.9 \"GET /api/health HTTP/1.1\" 200 17",
		"web1 node[4410]: served /api/orders in 38 ms",
		"web1 systemd[1]: Started session-{0}.scope - Session {0} of User deploy.",
		"web1 CRON[{0}]: (root) CMD (command -v debian-sa1 > /dev/null && debian-sa1 1 1)",
		"web1 node[4411]: mail queue: 3 sent, 0 waiting",
		"web1 sshd[{0}]: Accepted publickey for deploy from 203.0.113.9 port 51234 ssh2",
		"web1 kernel: [UFW BLOCK] IN=eth0 OUT= SRC=198.51.100.23 DST=10.0.0.5 PROTO=TCP DPT=23",
		"web1 postgres[1312]: LOG:  checkpoint complete: wrote 412 buffers (2.5%)",
		"web1 backup-agent[{0}]: error: destination s3://backups not reachable",
		"web1 systemd[1]: backup-agent.service: Failed with result 'exit-code'.",
	];

	/// <summary>The journal's last lines and the cursors around them, as <c>logs.journal</c> prints them.</summary>
	private static string Journal(string command, DateTimeOffset now)
	{
		long newest = JournalIndex(now);
		long count = Math.Clamp(Number(JournalCountPattern(), command) ?? 200, 1, 2000);
		StringBuilder text = new();
		text.Append("@@mt:anchor\n-- cursor: ").Append(Cursor(newest)).Append("\n@@mt:lines\n");
		for (long index = Math.Max(0, newest - count + 1); index <= newest; index++)
		{
			text.Append(JournalLine(index)).Append('\n');
		}

		return text.Append("-- cursor: ").Append(Cursor(newest)).Append('\n').ToString();
	}

	/// <summary>The entries after the cursor the follow names, then the newest cursor; nothing when none are new.</summary>
	private static string FollowJournal(string command, DateTimeOffset now)
	{
		long newest = JournalIndex(now);
		long after = CursorPattern().Match(command) is { Success: true } match
			&& long.TryParse(match.Groups[1].Value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out long index)
				? index
				: newest;
		if (after >= newest)
		{
			return "";
		}

		StringBuilder text = new();
		for (long next = Math.Max(after + 1, newest - 1999); next <= newest; next++)
		{
			text.Append(JournalLine(next)).Append('\n');
		}

		return text.Append("-- cursor: ").Append(Cursor(newest)).Append('\n').ToString();
	}

	private static long JournalIndex(DateTimeOffset now) => Math.Max(0, (long)((now - JournalStart).TotalSeconds / JournalEvery));

	private static string Cursor(long index) => string.Create(CultureInfo.InvariantCulture, $"s=6d656d6f;i={index:x};b=0d3f7a1c");

	private static string JournalLine(long index)
	{
		DateTimeOffset at = JournalStart.AddSeconds(index * JournalEvery);
		string message = string.Format(CultureInfo.InvariantCulture, JournalMessages[index % JournalMessages.Length], 40_000 + (index % 9_000));
		return string.Create(CultureInfo.InvariantCulture, $"{at:yyyy-MM-ddTHH:mm:ss}+00:00 {message}");
	}

	/// <summary>
	/// <c>logs.file</c>: the file's inode and size, then its last lines up to that size. A demo log starts afresh every
	/// day at midnight UTC under a new inode, which is when a follow sees it rotate.
	/// </summary>
	private static string ReadFile(string command, DateTimeOffset now)
	{
		string path = Assigned(command, "mt_file") ?? "/var/log/syslog";
		(long inode, long lines) = FileState(now);
		long count = Math.Clamp(Number(TailPattern(), command) ?? 200, 1, 2000);
		StringBuilder text = new();
		text.Append(CultureInfo.InvariantCulture, $"@@mt:file {inode} {lines * FileLineBytes}\n@@mt:lines\n");
		for (long index = Math.Max(0, lines - count); index < lines; index++)
		{
			text.Append(FileLine(path, now, index));
		}

		return text.ToString();
	}

	/// <summary><c>logs.file.follow</c>: what was added since the offset the script names, or the new file after a rotation.</summary>
	private static string FollowFile(string command, DateTimeOffset now)
	{
		string path = Assigned(command, "mt_file") ?? "/var/log/syslog";
		(long inode, long lines) = FileState(now);
		long size = lines * FileLineBytes;
		long from = Number(OffsetPattern(), command) ?? size;
		long known = Number(InodePattern(), command) ?? inode;
		StringBuilder text = new();
		text.Append(CultureInfo.InvariantCulture, $"@@mt:file {inode} {size}\n");
		if (known != inode || size < from)
		{
			text.Append("@@mt:rotated\n");
			from = 0;
		}

		text.Append("@@mt:lines\n");
		StringBuilder added = new();
		for (long index = from / FileLineBytes; index < lines; index++)
		{
			added.Append(FileLine(path, now, index));
		}

		return text.Append(added.ToString((int)(from % FileLineBytes), added.Length - (int)(from % FileLineBytes))).ToString();
	}

	/// <summary>Today's demo file: its inode, and how many lines it holds by now.</summary>
	private static (long Inode, long Lines) FileState(DateTimeOffset now)
	{
		DateTimeOffset midnight = new(now.UtcDateTime.Date, TimeSpan.Zero);
		return (131_000 + (long)(midnight - JournalStart).TotalDays, 1 + (long)((now - midnight).TotalSeconds / FileEvery));
	}

	/// <summary>Line <paramref name="index"/> of today's demo file, in the style of the log it stands for, padded to <see cref="FileLineBytes"/>.</summary>
	private static string FileLine(string path, DateTimeOffset now, long index)
	{
		DateTimeOffset at = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero).AddSeconds(index * FileEvery);
		long request = 88_000 + index;
		string line = path switch
		{
			_ when path.EndsWith("access.log", StringComparison.Ordinal) => string.Create(CultureInfo.InvariantCulture, $"203.0.113.{index % 200 + 1} - - [{at:dd/MMM/yyyy:HH:mm:ss} +0000] \"GET /api/items/{index % 97} HTTP/1.1\" {(index % 23 == 0 ? 404 : 200)} {512 + (index % 900)}"),
			_ when path.EndsWith("error.log", StringComparison.Ordinal) => string.Create(CultureInfo.InvariantCulture, $"{at:yyyy/MM/dd HH:mm:ss} [warn] 1124#1124: *{request} upstream response is buffered to a temporary file"),
			_ when path.EndsWith("auth.log", StringComparison.Ordinal) => string.Create(CultureInfo.InvariantCulture, $"{at:yyyy-MM-ddTHH:mm:ss}+00:00 web1 sshd[{40_000 + (index % 9_000)}]: Connection closed by 198.51.100.{index % 250} port {30_000 + (index % 20_000)}"),
			_ => string.Create(CultureInfo.InvariantCulture, $"{at:yyyy-MM-ddTHH:mm:ss}+00:00 web1 demo[{4410 + (index % 3)}]: heartbeat {index} from {path}"),
		};

		return line.Length >= FileLineBytes - 1 ? line[..(FileLineBytes - 1)] + "\n" : line.PadRight(FileLineBytes - 1) + "\n";
	}

	[GeneratedRegex(@"--after-cursor 's=[0-9a-f]+;i=([0-9a-f]+);", RegexOptions.CultureInvariant)]
	private static partial Regex CursorPattern();

	[GeneratedRegex(@"--show-cursor -n ([0-9]+)", RegexOptions.CultureInvariant)]
	private static partial Regex JournalCountPattern();

	[GeneratedRegex(@"[|] tail -n ([0-9]+)", RegexOptions.CultureInvariant)]
	private static partial Regex TailPattern();

	[GeneratedRegex(@"^mt_from=([0-9]+)$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
	private static partial Regex OffsetPattern();

	[GeneratedRegex(@"\[ ""\$1"" != '([0-9]+)' \]", RegexOptions.CultureInvariant)]
	private static partial Regex InodePattern();
}
