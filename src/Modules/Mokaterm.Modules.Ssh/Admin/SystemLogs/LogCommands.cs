using System.Globalization;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Modules.Ssh.Tools;

namespace Mokaterm.Modules.Ssh.Admin.SystemLogs;

/// <summary>
/// Reading the journal with filters, or the files under <c>/var/log</c> where there is no journal, and following
/// either: the journal from the cursor of the last entry shown, a file from the byte offset where the last read
/// stopped.
/// </summary>
internal static class LogCommands
{
	/// <summary>journalctl's priority names, most severe first. A filter shows its level and everything above it.</summary>
	public static readonly IReadOnlyList<string> Priorities = ["emerg", "alert", "crit", "err", "warning", "notice", "info", "debug"];

	/// <summary>The line counts the page offers; more would not be read in a dialog anyway.</summary>
	public static readonly IReadOnlyList<int> LineCounts = [50, 200, 500, 1000, 2000];

	public const int MaxLines = 2000;

	/// <summary>How often a followed log asks for what is new.</summary>
	public static readonly TimeSpan FollowInterval = TimeSpan.FromSeconds(2);

	/// <summary>The most of a file the first read looks through for its last lines; <c>tail</c> alone would read all of a huge one.</summary>
	public const int FileWindowBytes = 1024 * 1024;

	/// <summary>The most a follow takes from a file in one poll; a log growing faster than that has the middle skipped, and says so.</summary>
	public const int FollowChunkBytes = 256 * 1024;

	private const string CursorPrefix = "-- cursor: ";

	/// <summary>A follow poll is a quick read; one that hangs is dropped and the next poll tries again.</summary>
	private static readonly TimeSpan FollowTimeout = TimeSpan.FromSeconds(15);

	/// <summary>
	/// The journal's last lines, newest last, for one unit and at one priority or above when given, in UTC so every
	/// timestamp has the same offset and sorts as text. The cursor of the newest entry comes first (the anchor), which
	/// a follow starts from when the filter matched nothing yet; journalctl prints the cursor of the last line it shows
	/// after it.
	/// </summary>
	public static AdminScript Journal(string? unit, string? priority, int lines, bool asRoot) => new()
	{
		Name = "logs.journal",
		AsRoot = asRoot,
		Body = string.Join('\n',
			AdminSections.Start("anchor"),
			"journalctl --no-pager -q -n 1 -o cat --show-cursor 2>/dev/null | tail -n 1",
			AdminSections.Start("lines"),

			// journalctl prints its "you are not seeing everything" hint on stderr; it belongs next to the lines.
			$"journalctl --no-pager -o short-iso --utc --show-cursor -n {Lines(lines)}{Filter(unit, priority)} 2>&1"),
	};

	/// <summary>
	/// What the journal took in after <paramref name="cursor"/>, at most <see cref="MaxLines"/> entries from there (a
	/// burst larger than that continues on the next poll). <c>--after-cursor</c> rather than <c>--since</c>, which older
	/// systemd refuses in its ISO form and which would show a line at the same second twice. Quiet, so an empty poll
	/// prints nothing; errors stay on stderr rather than becoming log lines.
	/// </summary>
	public static AdminScript FollowJournal(string cursor, string? unit, string? priority, bool asRoot)
	{
		if (!IsCursor(cursor))
		{
			throw new ArgumentException("That is not a journal cursor.", nameof(cursor));
		}

		return new AdminScript
		{
			Name = "logs.journal.follow",
			AsRoot = asRoot,
			Timeout = FollowTimeout,
			Body = $"journalctl --no-pager -q -o short-iso --utc --show-cursor --after-cursor {PosixShell.Quote(cursor)} -n {Lines(MaxLines)}{Filter(unit, priority)}",
		};
	}

	/// <summary>The lines journalctl printed and the cursor to follow from: the last line's, else the anchor's.</summary>
	public static JournalRead ParseJournal(string output)
	{
		ArgumentNullException.ThrowIfNull(output);
		string? anchor = null;
		string text = output;
		if (AdminSections.Rest(output, "lines") is { } rest)
		{
			anchor = CursorOf(AdminSections.Parse(output[..(output.Length - rest.Length)]).Get("anchor"));
			text = rest;
		}

		string? cursor = null;
		List<string> kept = [];
		foreach (string line in text.Split('\n'))
		{
			if (line.StartsWith(CursorPrefix, StringComparison.Ordinal))
			{
				cursor = CursorOf(line) ?? cursor;
			}
			else
			{
				kept.Add(line);
			}
		}

		return new JournalRead([.. LogLines.Read(string.Join('\n', kept), isError: false)], cursor ?? anchor);
	}

	/// <summary>
	/// A journal cursor as journalctl prints it: <c>s=...;i=...;b=...;m=...;t=...;x=...</c>, hex values in named
	/// fields. Nothing else goes on a command line as one.
	/// </summary>
	public static bool IsCursor(string? cursor) =>
		cursor is { Length: > 0 and <= 512 }
		&& cursor.All(character => char.IsAsciiLetterOrDigit(character) || character is '=' or ';');

	/// <summary>Plain text logs under <c>/var/log</c>; rotated and compressed copies and binary accounting files are left out.</summary>
	public static AdminScript ListFiles(bool asRoot) => new()
	{
		Name = "logs.files",
		AsRoot = asRoot,
		Body = string.Join(' ',
			"find /var/log -maxdepth 3 -type f",
			"! -name '*.gz' ! -name '*.xz' ! -name '*.bz2' ! -name '*.zst' ! -name '*.[0-9]' ! -name '*.old'",
			"! -name wtmp ! -name btmp ! -name lastlog ! -name faillog ! -name '*.journal' ! -name '*.journal~'",
			"2>/dev/null | sort | head -n 500"),
	};

	public static IReadOnlyList<string> ParseFiles(string output) =>
		[.. AdminSections.Lines(output).Select(line => line.Trim()).Where(path => AdminNames.CheckLogPath(path) is null)];

	/// <summary>
	/// A file's last lines, with its inode and size, read so the lines end exactly at that size: the follow continues
	/// from there and neither repeats nor misses a line written meanwhile. A file larger than
	/// <see cref="FileWindowBytes"/> is read from that far before its end, and the window's first line, which may be
	/// cut, is dropped. <c>ls -ldin</c> gives inode and size on GNU and BusyBox alike without reading the file.
	/// </summary>
	public static AdminScript ReadFile(string path, int lines, bool asRoot)
	{
		CheckPath(path);
		string window = FileWindowBytes.ToString(CultureInfo.InvariantCulture);
		string count = Lines(lines);
		return new AdminScript
		{
			Name = "logs.file",
			AsRoot = asRoot,
			Body = string.Join('\n', [
				.. FileHeader(path),
				"if [ ! -r \"$mt_file\" ] || [ -z \"$6\" ]; then tail -n 1 -- \"$mt_file\" >/dev/null; exit 1; fi",
				AdminSections.Start("lines"),
				$"if [ \"$6\" -gt {window} ]; then",
				$"\ttail -c +$(($6 - {window} + 1)) -- \"$mt_file\" | head -c {window} | sed 1d | tail -n {count}",
				"else",
				$"\thead -c \"$6\" -- \"$mt_file\" | tail -n {count}",
				"fi"]),
		};
	}

	/// <summary>
	/// What was added to a file since <paramref name="offset"/>. A file with another inode, or smaller than the offset,
	/// was rotated or truncated, and is read from its start; more than <see cref="FollowChunkBytes"/> at once is cut to
	/// its newest part.
	/// </summary>
	public static AdminScript FollowFile(string path, string inode, long offset, bool asRoot)
	{
		CheckPath(path);
		if (inode.Length is 0 or > 20 || !inode.All(char.IsAsciiDigit))
		{
			throw new ArgumentException("An inode is a number.", nameof(inode));
		}

		ArgumentOutOfRangeException.ThrowIfNegative(offset);
		string chunk = FollowChunkBytes.ToString(CultureInfo.InvariantCulture);
		return new AdminScript
		{
			Name = "logs.file.follow",
			AsRoot = asRoot,
			Timeout = FollowTimeout,
			OutputLimitBytes = FollowChunkBytes * 2,
			Body = string.Join('\n', [
				.. FileHeader(path),
				"[ -n \"$6\" ] || exit 0",
				$"mt_from={offset.ToString(CultureInfo.InvariantCulture)}",
				$"if [ \"$1\" != '{inode}' ] || [ \"$6\" -lt \"$mt_from\" ]; then {AdminSections.Start("rotated")}; mt_from=0; fi",
				$"if [ $(($6 - mt_from)) -gt {chunk} ]; then {AdminSections.Start("skipped")}; mt_from=$(($6 - {chunk})); fi",
				AdminSections.Start("lines"),
				"if [ \"$6\" -gt \"$mt_from\" ]; then tail -c +$((mt_from + 1)) -- \"$mt_file\" | head -c $(($6 - mt_from)); fi"]),
		};
	}

	/// <summary>A file read or follow: its inode and size when they could be read, whether it turned over, and the text.</summary>
	public static FileRead ParseFile(string output)
	{
		ArgumentNullException.ThrowIfNull(output);
		string? text = AdminSections.Rest(output, "lines");
		AdminSections header = AdminSections.Parse(text is null ? output : output[..(output.Length - text.Length)]);
		(string inode, long? size) = ("", null);
		foreach ((string value, _) in header.All("file"))
		{
			string[] words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			if (words.Length == 2 && words[0].All(char.IsAsciiDigit) && long.TryParse(words[1], NumberStyles.None, CultureInfo.InvariantCulture, out long bytes))
			{
				(inode, size) = (words[0], bytes);
			}

			break;
		}

		return new FileRead(inode, size, header.Has("rotated"), header.Has("skipped"), text ?? "");
	}

	private static string Lines(int lines) => Math.Clamp(lines, 1, MaxLines).ToString(CultureInfo.InvariantCulture);

	private static string Filter(string? unit, string? priority)
	{
		string filter = "";
		if (!string.IsNullOrEmpty(unit))
		{
			if (AdminNames.CheckService(unit) is { } problem)
			{
				throw new ArgumentException(problem, nameof(unit));
			}

			filter += $" -u {PosixShell.Quote(unit)}";
		}

		if (!string.IsNullOrEmpty(priority))
		{
			if (!Priorities.Contains(priority))
			{
				throw new ArgumentException("Pick a priority from the list.", nameof(priority));
			}

			filter += $" -p {priority}";
		}

		return filter;
	}

	private static void CheckPath(string path)
	{
		if (AdminNames.CheckLogPath(path) is { } problem)
		{
			throw new ArgumentException(problem, nameof(path));
		}
	}

	/// <summary>The file's inode and size in <c>$1</c> and <c>$6</c>, printed as the first part. Globbing is off so a name with a star in it stays one word.</summary>
	private static string[] FileHeader(string path) =>
	[
		"set -f",
		$"mt_file={PosixShell.Quote(path)}",
		"set -- $(ls -ldin -- \"$mt_file\" 2>/dev/null)",
		"echo \"@@mt:file $1 $6\"",
	];

	private static string? CursorOf(string? text)
	{
		string line = AdminSections.Lines(text).LastOrDefault() ?? "";
		string cursor = line.StartsWith(CursorPrefix, StringComparison.Ordinal) ? line[CursorPrefix.Length..].Trim() : "";
		return IsCursor(cursor) ? cursor : null;
	}
}
