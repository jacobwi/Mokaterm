using Mokaterm.UI.Terminal.Triggers;

namespace Mokaterm.UI.Settings.Pages.Triggers;

/// <summary>One rule's matches on a sample line and the color it paints them, or null for a rule that only notifies.</summary>
public sealed record TriggerLayer(IReadOnlyList<TriggerMatchRange> Matches, string? Color);
