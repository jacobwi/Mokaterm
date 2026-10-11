using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Terminal.Triggers;

namespace Mokaterm.UI.Terminal.Interop;

/// <summary>A trigger rule in the shape triggers.js reads: the color already resolved to <c>#rrggbb</c>, null for none.</summary>
internal sealed record TerminalJsTrigger(string Pattern, bool IgnoreCase, string? Color, bool Notify)
{
	public static TerminalJsTrigger From(TerminalTrigger rule) =>
		new(rule.Pattern, rule.IgnoreCase, TriggerPalette.Hex(rule.Highlight), rule.Notify);
}
