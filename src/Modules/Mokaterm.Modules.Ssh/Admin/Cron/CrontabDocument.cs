using System.Text.RegularExpressions;

namespace Mokaterm.Modules.Ssh.Admin.Cron;

/// <summary>
/// A whole crontab, line by line. Editing replaces, adds or removes entry lines and leaves every other line exactly as
/// it was, comments and variables included, so writing it back changes only what the user changed.
/// </summary>
internal sealed partial class CrontabDocument
{
	private CrontabDocument(IReadOnlyList<CrontabLine> lines, bool hasUserField)
	{
		Lines = lines;
		HasUserField = hasUserField;
	}

	public IReadOnlyList<CrontabLine> Lines { get; }

	/// <summary>True for <c>/etc/crontab</c> and <c>/etc/cron.d</c>, whose entries name a user after the schedule.</summary>
	public bool HasUserField { get; }

	/// <summary>Entry lines with their index in <see cref="Lines"/>.</summary>
	public IEnumerable<(int Index, CrontabEntry Entry)> Entries =>
		Lines.Select((line, index) => (line, index)).Where(pair => pair.line.Entry is not null).Select(pair => (pair.index, pair.line.Entry!));

	/// <summary>Lines cron would refuse, with their line numbers counted from 1.</summary>
	public IEnumerable<(int Number, CrontabLine Line)> Problems =>
		Lines.Select((line, index) => (index + 1, line)).Where(pair => pair.line.Kind == CrontabLineKind.Invalid);

	public static CrontabDocument Parse(string? text, bool hasUserField = false)
	{
		List<CrontabLine> lines = [];
		string normalized = (text ?? "").ReplaceLineEndings("\n");
		if (normalized.EndsWith('\n'))
		{
			normalized = normalized[..^1];
		}

		if (normalized.Length > 0)
		{
			foreach (string line in normalized.Split('\n'))
			{
				lines.Add(ParseLine(line, hasUserField));
			}
		}

		return new CrontabDocument(lines, hasUserField);
	}

	public static CrontabLine ParseLine(string text, bool hasUserField)
	{
		ArgumentNullException.ThrowIfNull(text);
		string trimmed = text.Trim();
		if (trimmed.Length == 0)
		{
			return new CrontabLine(CrontabLineKind.Blank, text);
		}

		if (trimmed[0] == '#')
		{
			return new CrontabLine(CrontabLineKind.Comment, text);
		}

		if (VariableLine().IsMatch(trimmed))
		{
			return new CrontabLine(CrontabLineKind.Variable, text);
		}

		List<(int Start, int End)> words = Words(trimmed);
		int scheduleWords = trimmed[0] == '@' ? 1 : 5;
		int commandStart = scheduleWords + (hasUserField ? 1 : 0);
		if (words.Count <= commandStart)
		{
			return new CrontabLine(CrontabLineKind.Invalid, text, Problem: hasUserField
				? "An entry here needs a schedule, a user and a command."
				: "An entry needs a schedule and a command.");
		}

		string schedule = trimmed[..words[scheduleWords - 1].End];
		if (CronSchedule.TryParse(schedule, out string? problem) is not { } parsed)
		{
			return new CrontabLine(CrontabLineKind.Invalid, text, Problem: problem);
		}

		string? user = hasUserField ? trimmed[words[scheduleWords].Start..words[scheduleWords].End] : null;
		string command = trimmed[words[commandStart].Start..];
		return new CrontabLine(CrontabLineKind.Entry, text, new CrontabEntry(parsed, command, user));
	}

	public CrontabDocument Replace(int index, CrontabEntry entry) => With(index, new CrontabLine(CrontabLineKind.Entry, entry.Format(), entry));

	public CrontabDocument Add(CrontabEntry entry)
	{
		ArgumentNullException.ThrowIfNull(entry);
		return new CrontabDocument([.. Lines, new CrontabLine(CrontabLineKind.Entry, entry.Format(), entry)], HasUserField);
	}

	public CrontabDocument Remove(int index)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(index);
		ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Lines.Count);
		List<CrontabLine> lines = [.. Lines];
		lines.RemoveAt(index);
		return new CrontabDocument(lines, HasUserField);
	}

	/// <summary>The crontab as crontab(1) takes it: one line each, and a line break after the last, which cron requires.</summary>
	public string ToText() => Lines.Count == 0 ? "" : string.Join('\n', Lines.Select(line => line.Text)) + "\n";

	private CrontabDocument With(int index, CrontabLine line)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(index);
		ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Lines.Count);
		List<CrontabLine> lines = [.. Lines];
		lines[index] = line;
		return new CrontabDocument(lines, HasUserField);
	}

	private static List<(int Start, int End)> Words(string text)
	{
		List<(int Start, int End)> words = [];
		int index = 0;
		while (index < text.Length)
		{
			while (index < text.Length && text[index] is ' ' or '\t')
			{
				index++;
			}

			int start = index;
			while (index < text.Length && text[index] is not (' ' or '\t'))
			{
				index++;
			}

			if (index > start)
			{
				words.Add((start, index));
			}
		}

		return words;
	}

	// cronie and Vixie cron both take blanks around the equals sign; a value may be quoted.
	[GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*\s*=", RegexOptions.CultureInvariant)]
	private static partial Regex VariableLine();
}
