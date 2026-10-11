namespace Mokaterm.UI.Terminal.Triggers;

/// <summary>Where a pattern matched in a sample line, in UTF-16 offsets as JavaScript counts them.</summary>
public sealed record TriggerMatchRange(int Start, int Length);
