using Mokaterm.Modules.Ssh.Tools;

namespace Mokaterm.Modules.Ssh.Admin.SystemLogs;

/// <summary>
/// Where a followed log stands and the script that reads on from there: a journal cursor, or a file's inode and the
/// byte offset its last read ended at. Each poll hands back only what is new, so the lines go to
/// <see cref="LogTail.AppendAll"/> without any de-duplication.
/// </summary>
/// <remarks>
/// A file can be read in the middle of a line its writer has not finished. That piece is held back and put in front of
/// the next poll's text, so a line is never shown in two halves; the offset itself always moves to the size read.
/// </remarks>
internal sealed class LogFollower
{
	/// <summary>A held-back piece longer than this is a file without line breaks, and is shown as it is.</summary>
	internal const int MaxPendingChars = 64 * 1024;

	private readonly string? _path;
	private readonly string? _unit;
	private readonly string? _priority;
	private readonly bool _asRoot;
	private string _cursor = "";
	private string _inode = "";
	private long _offset;
	private string _pending = "";

	private LogFollower(string? path, string? unit, string? priority, bool asRoot)
	{
		_path = path;
		_unit = unit;
		_priority = priority;
		_asRoot = asRoot;
	}

	/// <summary>Where the next poll starts, for a test or a log line: the cursor, or the inode and offset.</summary>
	public string Position => _path is null ? _cursor : $"{_inode}@{_offset}";

	/// <summary>Follows the journal from the read's cursor, with the read's filter; null when the journal gave no cursor.</summary>
	public static LogFollower? ForJournal(JournalRead read, string? unit, string? priority, bool asRoot)
	{
		ArgumentNullException.ThrowIfNull(read);
		return read.Cursor is { } cursor && LogCommands.IsCursor(cursor)
			? new LogFollower(null, unit, priority, asRoot) { _cursor = cursor }
			: null;
	}

	/// <summary>
	/// The complete lines of a file's first read, and a follower that continues where it ended; the follower is null when
	/// the file could not be listed.
	/// </summary>
	public static (IReadOnlyList<LogLine> Lines, LogFollower? Follower) ForFile(string path, FileRead read, bool asRoot)
	{
		ArgumentNullException.ThrowIfNull(path);
		ArgumentNullException.ThrowIfNull(read);
		LogFollower follower = new(path, null, null, asRoot);
		IReadOnlyList<LogLine> lines = follower.Complete(read.Text);
		if (read.Size is not { } size || read.Inode.Length == 0)
		{
			return (lines, null);
		}

		follower._inode = read.Inode;
		follower._offset = size;
		return (lines, follower);
	}

	/// <summary>The script that reads what came after the position.</summary>
	public AdminScript Next() => _path is null
		? LogCommands.FollowJournal(_cursor, _unit, _priority, _asRoot)
		: LogCommands.FollowFile(_path, _inode, _offset, _asRoot);

	/// <summary>Reads a poll's outcome, moves the position on and hands back the new lines with anything worth saying.</summary>
	/// <exception cref="AdminException">The poll failed; the position stays where it was.</exception>
	public LogFollowStep Take(AdminOutcome outcome)
	{
		ArgumentNullException.ThrowIfNull(outcome);
		if (!outcome.Succeeded)
		{
			throw new AdminException(outcome.Problem());
		}

		if (_path is null)
		{
			JournalRead read = LogCommands.ParseJournal(outcome.Output);
			if (read.Cursor is { } cursor)
			{
				_cursor = cursor;
			}

			return new LogFollowStep(read.Lines, null);
		}

		FileRead file = LogCommands.ParseFile(outcome.Output);
		if (file.Size is not { } size || file.Inode.Length == 0)
		{
			return new LogFollowStep([], "The file is not there right now; following picks it up again when it comes back.");
		}

		string? notice = null;
		string text = file.Text;
		if (file.Rotated)
		{
			_pending = "";
			notice = "The file was rotated or emptied, so following continues from the start of the new one.";
		}

		if (file.Skipped)
		{
			// The chunk starts somewhere in a line, and what was held back belongs to text that was skipped.
			_pending = "";
			int firstBreak = text.IndexOf('\n', StringComparison.Ordinal);
			text = firstBreak < 0 ? "" : text[(firstBreak + 1)..];
			notice = "Lines came faster than one read takes, so some in between were skipped.";
		}

		_inode = file.Inode;
		_offset = size;
		return new LogFollowStep(Complete(text), notice);
	}

	/// <summary>The whole lines of what is held back plus <paramref name="text"/>; a piece after the last line break is held back.</summary>
	private List<LogLine> Complete(string text)
	{
		string all = _pending + text;
		int lastBreak = all.LastIndexOf('\n');
		if (lastBreak < 0 && all.Length <= MaxPendingChars)
		{
			_pending = all;
			return [];
		}

		string whole = lastBreak < 0 ? all : all[..(lastBreak + 1)];
		_pending = lastBreak < 0 ? "" : all[(lastBreak + 1)..];
		if (_pending.Length > MaxPendingChars)
		{
			whole += _pending + "\n";
			_pending = "";
		}

		return [.. LogLines.Read(whole, isError: false)];
	}
}
