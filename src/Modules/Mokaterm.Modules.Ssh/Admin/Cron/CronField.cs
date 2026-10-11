using System.Globalization;

namespace Mokaterm.Modules.Ssh.Admin.Cron;

/// <summary>
/// One time field, parsed the way cron reads it: <c>*</c>, a value, a range, a step over either, and lists of those.
/// Month and weekday names (<c>jan</c>, <c>mon</c>) are accepted, and a weekday of 7 is Sunday like 0.
/// </summary>
internal sealed class CronField
{
	private static readonly string[] MonthNames = ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"];

	private static readonly string[] DayNames = ["sun", "mon", "tue", "wed", "thu", "fri", "sat"];

	private readonly bool[] _values;

	private CronField(CronFieldKind kind, string text, bool[] values, int? everyStep, IReadOnlyList<(int From, int To, int Step)> parts)
	{
		Kind = kind;
		Text = text;
		_values = values;
		EveryStep = everyStep;
		Parts = parts;
	}

	public CronFieldKind Kind { get; }

	/// <summary>The field as written.</summary>
	public string Text { get; }

	/// <summary>
	/// Written from <c>*</c>, with or without a step. cron decides between "either day field" and "both day fields" on
	/// this, not on whether every day matches: <c>*/2</c> in the day of the month still counts as a star.
	/// </summary>
	public bool IsStarred => Text.StartsWith('*');

	/// <summary>True for <c>*</c> (and <c>*/1</c>): every value.</summary>
	public bool IsAll => Values.Count == Max(Kind) - Min(Kind) + 1;

	/// <summary>The step of a field written <c>*/n</c>, or null.</summary>
	public int? EveryStep { get; }

	/// <summary>The list items in the order written, each as a range with its step; a single value is a range of one.</summary>
	public IReadOnlyList<(int From, int To, int Step)> Parts { get; }

	/// <summary>Every value the field matches, ascending. Weekday 7 is folded into 0.</summary>
	public IReadOnlyList<int> Values => [.. Enumerable.Range(0, _values.Length).Where(value => _values[value])];

	/// <summary>The one value the field matches, or null when it matches several.</summary>
	public int? Single => Values is [var only] && !IsAll ? only : null;

	public static int Min(CronFieldKind kind) => kind is CronFieldKind.DayOfMonth or CronFieldKind.Month ? 1 : 0;

	public static int Max(CronFieldKind kind) => kind switch
	{
		CronFieldKind.Minute => 59,
		CronFieldKind.Hour => 23,
		CronFieldKind.DayOfMonth => 31,
		CronFieldKind.Month => 12,
		_ => 6,
	};

	/// <summary>Parses <paramref name="text"/>, or returns why it is not a valid field.</summary>
	public static CronField? TryParse(CronFieldKind kind, string text, out string? problem)
	{
		ArgumentNullException.ThrowIfNull(text);
		problem = null;
		if (text.Length == 0)
		{
			problem = $"The {Name(kind)} field is empty.";
			return null;
		}

		// Weekday 7 is Sunday, so parsing allows it and folding below maps it onto 0.
		int min = Min(kind);
		int max = kind == CronFieldKind.DayOfWeek ? 7 : Max(kind);
		bool[] values = new bool[Max(kind) + 1];
		List<(int From, int To, int Step)> parts = [];
		int? everyStep = null;
		foreach (string item in text.Split(','))
		{
			if (!TryParseItem(kind, item, min, max, out int from, out int to, out int step, out bool star, out problem))
			{
				return null;
			}

			if (star && text.IndexOf(',', StringComparison.Ordinal) < 0)
			{
				everyStep = step;
			}

			parts.Add((from, to, step));
			for (int value = from; value <= to; value += step)
			{
				values[kind == CronFieldKind.DayOfWeek && value == 7 ? 0 : value] = true;
			}
		}

		return new CronField(kind, text, values, everyStep is 1 ? null : everyStep, parts);
	}

	public bool Matches(int value) => value >= 0 && value < _values.Length && _values[value];

	/// <summary>"minute", "hour", "day of the month", "month", "day of the week".</summary>
	public static string Name(CronFieldKind kind) => kind switch
	{
		CronFieldKind.Minute => "minute",
		CronFieldKind.Hour => "hour",
		CronFieldKind.DayOfMonth => "day of the month",
		CronFieldKind.Month => "month",
		_ => "day of the week",
	};

	private static bool TryParseItem(CronFieldKind kind, string item, int min, int max, out int from, out int to, out int step, out bool star, out string? problem)
	{
		from = min;
		to = max;
		step = 1;
		star = false;
		problem = null;

		string range = item;
		int slash = item.IndexOf('/', StringComparison.Ordinal);
		if (slash >= 0)
		{
			range = item[..slash];
			if (!int.TryParse(item[(slash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out step) || step < 1 || step > max)
			{
				problem = $"\"{item}\" has a step that is not a number from 1 to {max} in the {Name(kind)} field.";
				return false;
			}
		}

		if (range == "*")
		{
			star = true;
			if (kind == CronFieldKind.DayOfWeek)
			{
				to = 6;
			}

			return true;
		}

		int dash = range.IndexOf('-', StringComparison.Ordinal);
		string first = dash < 0 ? range : range[..dash];
		if (!TryValue(kind, first, out from))
		{
			problem = Invalid(kind, item, min, max);
			return false;
		}

		if (dash < 0)
		{
			// A step on a single value ("5/10") is a cronie extension the other crons refuse.
			if (slash >= 0)
			{
				problem = $"\"{item}\" puts a step on a single value; write a range such as {from}-{max}/{step}.";
				return false;
			}

			to = from;
		}
		else if (!TryValue(kind, range[(dash + 1)..], out to))
		{
			problem = Invalid(kind, item, min, max);
			return false;
		}

		if (from < min || to > max || from > to)
		{
			problem = from > to && from <= max && to >= min
				? $"\"{item}\" runs backwards in the {Name(kind)} field."
				: Invalid(kind, item, min, max);
			return false;
		}

		return true;
	}

	private static bool TryValue(CronFieldKind kind, string text, out int value)
	{
		if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value))
		{
			return true;
		}

		string lower = text.ToLowerInvariant();
		int index = kind switch
		{
			CronFieldKind.Month => Array.IndexOf(MonthNames, lower),
			CronFieldKind.DayOfWeek => Array.IndexOf(DayNames, lower),
			_ => -1,
		};

		value = kind == CronFieldKind.Month ? index + 1 : index;
		return index >= 0;
	}

	private static string Invalid(CronFieldKind kind, string item, int min, int max) => kind switch
	{
		CronFieldKind.Month => $"\"{item}\" is not a month: use 1 to 12 or jan to dec.",
		CronFieldKind.DayOfWeek => $"\"{item}\" is not a day of the week: use 0 to 7 (0 and 7 are Sunday) or sun to sat.",
		_ => $"\"{item}\" is not a valid {Name(kind)}: use {min} to {max}.",
	};
}
