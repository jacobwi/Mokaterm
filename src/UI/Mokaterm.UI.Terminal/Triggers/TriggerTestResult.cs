namespace Mokaterm.UI.Terminal.Triggers;

/// <summary>
/// One rule run on a sample line in the page, the way a terminal would run it. <see cref="Error"/> says why a rule that
/// is not <see cref="Valid"/> would be skipped: JavaScript refused the pattern, it repeats a repeat, or it was too slow.
/// </summary>
public sealed record TriggerTestResult(bool Valid, string? Error, IReadOnlyList<TriggerMatchRange> Matches)
{
	public bool IsMatch => Valid && Matches is { Count: > 0 };
}
