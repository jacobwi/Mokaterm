namespace Mokaterm.Modules.Ssh.Admin.Cron;

/// <summary>When a crontab entry runs: five time fields, or one of the <c>@</c> nicknames.</summary>
internal sealed class CronSchedule
{
	/// <summary>The nicknames every common cron takes, and the five fields each stands for (null for <c>@reboot</c>).</summary>
	public static readonly IReadOnlyDictionary<string, string?> Nicknames = new Dictionary<string, string?>(StringComparer.Ordinal)
	{
		["@reboot"] = null,
		["@yearly"] = "0 0 1 1 *",
		["@annually"] = "0 0 1 1 *",
		["@monthly"] = "0 0 1 * *",
		["@weekly"] = "0 0 * * 0",
		["@daily"] = "0 0 * * *",
		["@midnight"] = "0 0 * * *",
		["@hourly"] = "0 * * * *",
	};

	private CronSchedule(string text, string? nickname, IReadOnlyList<CronField> fields)
	{
		Text = text;
		Nickname = nickname;
		Fields = fields;
	}

	/// <summary>As written: the nickname, or the five fields separated by single spaces.</summary>
	public string Text { get; }

	public string? Nickname { get; }

	/// <summary>The five fields, empty for <c>@reboot</c>.</summary>
	public IReadOnlyList<CronField> Fields { get; }

	public bool IsReboot => Nickname == "@reboot";

	public CronField Minute => Fields[0];

	public CronField Hour => Fields[1];

	public CronField DayOfMonth => Fields[2];

	public CronField Month => Fields[3];

	public CronField DayOfWeek => Fields[4];

	/// <summary>A nickname, or exactly five fields separated by blanks.</summary>
	public static CronSchedule? TryParse(string text, out string? problem)
	{
		ArgumentNullException.ThrowIfNull(text);
		string trimmed = text.Trim();
		if (trimmed.StartsWith('@'))
		{
			string lower = trimmed.ToLowerInvariant();
			if (!Nicknames.TryGetValue(lower, out string? fields))
			{
				problem = $"{trimmed} is not a schedule cron knows. Use @reboot, @hourly, @daily, @weekly, @monthly or @yearly.";
				return null;
			}

			problem = null;
			return fields is null
				? new CronSchedule(lower, lower, [])
				: new CronSchedule(lower, lower, ParseFields(fields.Split(' '), out _)!);
		}

		string[] parts = trimmed.Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length != 5)
		{
			problem = "A schedule has five fields: minute, hour, day of the month, month and day of the week.";
			return null;
		}

		IReadOnlyList<CronField>? parsed = ParseFields(parts, out problem);
		return parsed is null ? null : new CronSchedule(string.Join(' ', parts), null, parsed);
	}

	/// <summary>The five fields parsed one by one, or the first one's problem.</summary>
	public static IReadOnlyList<CronField>? ParseFields(IReadOnlyList<string> parts, out string? problem)
	{
		ArgumentNullException.ThrowIfNull(parts);
		List<CronField> fields = [];
		for (int index = 0; index < 5; index++)
		{
			if (CronField.TryParse((CronFieldKind)index, parts[index], out problem) is not { } field)
			{
				return null;
			}

			fields.Add(field);
		}

		problem = null;
		return fields;
	}

	/// <summary>
	/// The next <paramref name="count"/> times the entry runs after <paramref name="after"/>, in the same wall-clock
	/// time as <paramref name="after"/> (the server's). Daylight saving shifts are not modelled, and a schedule that
	/// never matches (the 30th of February) gives none within five years. <c>@reboot</c> has none.
	/// </summary>
	public IReadOnlyList<DateTime> NextRuns(DateTime after, int count)
	{
		List<DateTime> runs = [];
		if (IsReboot || count <= 0)
		{
			return runs;
		}

		IReadOnlyList<int> hours = Hour.Values;
		IReadOnlyList<int> minutes = Minute.Values;
		DateTime start = new(after.Year, after.Month, after.Day, after.Hour, after.Minute, 0, after.Kind);
		DateTime limit = after.Date.AddYears(5);
		for (DateTime day = after.Date; day <= limit; day = day.AddDays(1))
		{
			if (!Month.Matches(day.Month) || !DayMatches(day))
			{
				continue;
			}

			foreach (int hour in hours)
			{
				foreach (int minute in minutes)
				{
					DateTime run = day.AddHours(hour).AddMinutes(minute);
					if (run > start)
					{
						runs.Add(run);
						if (runs.Count == count)
						{
							return runs;
						}
					}
				}
			}
		}

		return runs;
	}

	/// <summary>The schedule in words, such as "Every day at 03:00".</summary>
	public string Describe() => CronDescriber.Describe(this);

	public override string ToString() => Text;

	// Vixie cron and cronie: when either day field is starred both must match, otherwise either one will do.
	private bool DayMatches(DateTime day)
	{
		bool dayOfMonth = DayOfMonth.Matches(day.Day);
		bool dayOfWeek = DayOfWeek.Matches((int)day.DayOfWeek);
		return DayOfMonth.IsStarred || DayOfWeek.IsStarred ? dayOfMonth && dayOfWeek : dayOfMonth || dayOfWeek;
	}
}
