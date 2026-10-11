namespace Mokaterm.Abstractions.Settings;

/// <summary>
/// One trigger: a JavaScript regular expression that runs on each line a terminal prints, and what happens to a line it
/// matches. The pattern is checked where it is typed, because only the page can say whether JavaScript accepts it; a
/// pattern that does not compile is skipped where the terminal runs it.
/// </summary>
public sealed record TerminalTrigger
{
	/// <summary>Keeps a rule apart from the others while the list is edited, reordered or changed from another window.</summary>
	public Guid Id { get; init; }

	public string Pattern { get; init; } = "";

	public bool IgnoreCase { get; init; }

	/// <summary>The color the matched text is drawn in, or <see cref="TriggerColor.None"/> to leave it alone.</summary>
	public TriggerColor Highlight { get; init; } = TriggerColor.None;

	/// <summary>Show a notice with the matched line, at most once every <see cref="TerminalTriggerSettings.NotifyIntervalSeconds"/>.</summary>
	public bool Notify { get; init; }

	/// <summary>False keeps the rule without running it.</summary>
	public bool Enabled { get; init; } = true;

	/// <summary>True when the rule has something to do with a match. One that neither highlights nor notifies never runs.</summary>
	public bool HasAction => Highlight != TriggerColor.None || Notify;
}
