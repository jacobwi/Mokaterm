using System.Globalization;

namespace Mokaterm.Modules.Ssh.Admin.Cron;

/// <summary>
/// A schedule in plain words, for the preview under the editor: "Every day at 03:00", "Every 5 minutes, on Monday to
/// Friday". The common shapes get a sentence of their own; anything else falls back to the field text, which is still
/// right, only less friendly. When both day fields are set and neither starts with a star, cron runs on either, and the
/// sentence says "or".
/// </summary>
internal static class CronDescriber
{
	private const int MaxListed = 6;

	private static readonly string[] MonthNames =
	[
		"January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December",
	];

	private static readonly string[] DayNames = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

	public static string Describe(CronSchedule schedule)
	{
		ArgumentNullException.ThrowIfNull(schedule);
		switch (schedule.Nickname)
		{
			case "@reboot":
				return "At every boot";
			case "@yearly" or "@annually":
				return "Every year on 1 January at 00:00";
			case "@monthly":
				return "Every month on day 1 at 00:00";
			case "@weekly":
				return "Every Sunday at 00:00";
			case "@daily" or "@midnight":
				return "Every day at 00:00";
			case "@hourly":
				return "Every hour, on the hour";
		}

		string time = TimePhrase(schedule.Minute, schedule.Hour);
		bool atTime = time.StartsWith("at ", StringComparison.Ordinal);
		CronField dom = schedule.DayOfMonth;
		CronField dow = schedule.DayOfWeek;
		CronField month = schedule.Month;

		if (dom.IsAll && dow.IsAll && month.IsAll)
		{
			return atTime ? "Every day " + time : Capitalize(time);
		}

		if (atTime && dom.IsAll && month.IsAll)
		{
			return dow.Single is { } day ? $"Every {DayNames[day]} {time}" : $"{Capitalize(Weekdays(dow))} {time}";
		}

		if (atTime && dow.IsAll && dom.Single is { } dayOfMonth)
		{
			if (month.IsAll)
			{
				return $"Every month on day {dayOfMonth} {time}";
			}

			if (month.Single is { } single)
			{
				return $"Every year on {dayOfMonth} {MonthNames[single - 1]} {time}";
			}
		}

		List<string> parts = [Capitalize(time)];
		if (DayPhrase(dom, dow) is { Length: > 0 } days)
		{
			parts.Add(days);
		}

		if (!month.IsAll)
		{
			parts.Add(MonthPhrase(month));
		}

		return string.Join(", ", parts);
	}

	private static string TimePhrase(CronField minute, CronField hour)
	{
		if (minute.Single is { } m)
		{
			if (hour.Single is { } h)
			{
				return $"at {Clock(h, m)}";
			}

			if (hour.IsAll)
			{
				return m == 0 ? "every hour, on the hour" : $"every hour at minute {m}";
			}

			if (hour.EveryStep is { } hourStep)
			{
				return m == 0 ? $"every {hourStep} hours, on the hour" : $"every {hourStep} hours at minute {m}";
			}

			if (Range(hour) is ({ } from, { } to))
			{
				return $"every hour from {Clock(from, m)} to {Clock(to, m)}";
			}

			IReadOnlyList<int> hours = hour.Values;
			return hours.Count <= MaxListed
				? "at " + Join(hours.Select(value => Clock(value, m)))
				: $"at minute {m} past hours {hour.Text}";
		}

		string every = minute.IsAll ? "every minute" : minute.EveryStep is { } step ? $"every {step} minutes" : "";
		if (every.Length > 0)
		{
			if (hour.IsAll)
			{
				return every;
			}

			if (hour.Single is { } h)
			{
				return $"{every} from {Clock(h, 0)} to {Clock(h, 59)}";
			}

			if (Range(hour) is ({ } from, { } to))
			{
				return $"{every} from {Clock(from, 0)} to {Clock(to, 59)}";
			}

			return $"{every} during hours {HourList(hour)}";
		}

		IReadOnlyList<int> minutes = minute.Values;
		if (hour.Single is { } onlyHour && minutes.Count <= MaxListed)
		{
			return "at " + Join(minutes.Select(value => Clock(onlyHour, value)));
		}

		if (hour.IsAll && minutes.Count <= MaxListed * 2)
		{
			return $"every hour at minutes {Join(minutes.Select(Number))}";
		}

		return $"at minutes {minute.Text} past {(hour.IsAll ? "every hour" : "hours " + HourList(hour))}";
	}

	private static string DayPhrase(CronField dom, CronField dow)
	{
		string? days = dom.IsAll ? null : DaysOfMonth(dom);
		string? weekdays = dow.IsAll ? null : Weekdays(dow);
		return (days, weekdays) switch
		{
			(null, null) => "",
			({ } d, null) => "on " + d,
			(null, { } w) => "on " + w,
			({ } d, { } w) when !dom.IsStarred && !dow.IsStarred => $"on {d} or on {w}",
			({ } d, { } w) => $"on {d}, when it falls on {w}",
		};
	}

	private static string DaysOfMonth(CronField dom)
	{
		if (dom.Single is { } day)
		{
			return $"day {day} of the month";
		}

		if (dom.EveryStep is { } step)
		{
			return $"every {Ordinal(step)} day of the month";
		}

		if (Range(dom) is ({ } from, { } to))
		{
			return $"days {from} to {to} of the month";
		}

		IReadOnlyList<int> values = dom.Values;
		return values.Count <= MaxListed ? $"days {Join(values.Select(Number))} of the month" : $"days {dom.Text} of the month";
	}

	private static string Weekdays(CronField dow)
	{
		if (dow.Single is { } day)
		{
			return DayNames[day];
		}

		IReadOnlyList<int> values = dow.Values;
		if (Range(dow) is ({ } from, { } to) && to <= 6)
		{
			return $"{DayNames[from]} to {DayNames[to]}";
		}

		if (values is [0, 6])
		{
			return "Saturday and Sunday";
		}

		return Join(values.Select(value => DayNames[value]));
	}

	private static string MonthPhrase(CronField month)
	{
		if (month.Single is { } single)
		{
			return "in " + MonthNames[single - 1];
		}

		if (Range(month) is ({ } from, { } to))
		{
			return $"from {MonthNames[from - 1]} to {MonthNames[to - 1]}";
		}

		IReadOnlyList<int> values = month.Values;
		return values.Count <= MaxListed ? "in " + Join(values.Select(value => MonthNames[value - 1])) : $"in months {month.Text}";
	}

	/// <summary>The bounds of a field written as one plain range, such as <c>9-17</c>.</summary>
	private static (int? From, int? To) Range(CronField field) =>
		field.Parts is [var only] && only.Step == 1 && only.From < only.To && !field.IsAll ? (only.From, only.To) : (null, null);

	private static string HourList(CronField hour)
	{
		IReadOnlyList<int> values = hour.Values;
		return values.Count <= MaxListed ? Join(values.Select(Number)) : hour.Text;
	}

	private static string Clock(int hour, int minute) => string.Create(CultureInfo.InvariantCulture, $"{hour:00}:{minute:00}");

	private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

	private static string Ordinal(int value)
	{
		string suffix = (value % 100) is 11 or 12 or 13 ? "th" : (value % 10) switch
		{
			1 => "st",
			2 => "nd",
			3 => "rd",
			_ => "th",
		};

		return Number(value) + suffix;
	}

	private static string Join(IEnumerable<string> items)
	{
		List<string> list = [.. items];
		return list.Count switch
		{
			0 => "",
			1 => list[0],
			_ => string.Join(", ", list.Take(list.Count - 1)) + " and " + list[^1],
		};
	}

	private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
