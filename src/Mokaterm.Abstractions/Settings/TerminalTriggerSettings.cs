namespace Mokaterm.Abstractions.Settings;

/// <summary>
/// Triggers: patterns that run on what every terminal prints, to color a match or raise a notice. One list for all
/// sessions, kept out of <see cref="TerminalSettings"/> so that resetting the terminal page leaves the rules alone and a
/// connection's own font or theme never touches them. Nothing here is a secret.
/// </summary>
public sealed record TerminalTriggerSettings : ISettingsSection
{
	/// <summary>Rules past this many are dropped: every rule runs on every new line of every session.</summary>
	public const int MaxRules = 20;

	/// <summary>Patterns longer than this are dropped. A rule is a line filter, not a program.</summary>
	public const int MaxPatternLength = 200;

	/// <summary>The shortest time between two notices from one rule in one terminal, so a looping error cannot bury the screen.</summary>
	public const int NotifyIntervalSeconds = 5;

	public static string SectionKey => "triggers";

	/// <summary>In the order they run. A later rule's color wins where two highlight the same text.</summary>
	public IReadOnlyList<TerminalTrigger> Rules { get; init; } = [];

	/// <summary>
	/// Where the host can, a rule's notice also goes to the operating system while the window holding the sessions is
	/// minimized or behind another one, so it does not expire unseen. A desktop setting: the web host has nothing to show
	/// it with.
	/// </summary>
	public bool SystemNotifications { get; init; } = true;

	/// <summary>
	/// Whether that notification carries the matched line. Off by default: the operating system keeps notifications in a
	/// store of its own, and a line from a server can hold a token.
	/// </summary>
	public bool SystemNotificationShowsLine { get; init; }

	/// <summary>The rules a terminal runs: enabled, with something to do.</summary>
	public IEnumerable<TerminalTrigger> Active => (Rules ?? []).Where(rule => rule is { Enabled: true, HasAction: true });

	/// <summary>True when <paramref name="pattern"/> fits the length cap and holds something to match.</summary>
	public static bool IsWithinLimits(string? pattern) => pattern is { Length: > 0 and <= MaxPatternLength };

	/// <summary>
	/// A copy holding at most <see cref="MaxRules"/> rules, none of them with a pattern past <see cref="MaxPatternLength"/>.
	/// What is over is dropped rather than cut: half a pattern matches something else, or nothing at all. The same list
	/// comes back when nothing was over, which keeps record equality for an update that changed nothing.
	/// </summary>
	public TerminalTriggerSettings Clamped()
	{
		if (Rules is null)
		{
			return this with { Rules = [] };
		}

		List<TerminalTrigger> kept = new(Math.Min(Rules.Count, MaxRules));
		foreach (TerminalTrigger? rule in Rules)
		{
			if (kept.Count == MaxRules)
			{
				break;
			}

			if (rule is not null && IsWithinLimits(rule.Pattern))
			{
				kept.Add(rule);
			}
		}

		return kept.Count == Rules.Count ? this : this with { Rules = kept };
	}
}
