using Mokaterm.UI.Common.Components;

namespace Mokaterm.UI.Shell;

/// <summary>One figure of the status bar's host figures, such as <c>CPU 12%</c>, with how full it is.</summary>
/// <param name="Label">The short name, such as <c>CPU</c>.</param>
/// <param name="Value">The share, such as <c>12%</c>.</param>
/// <param name="Level">The colour, by the rule every usage bar follows.</param>
internal sealed record HostFigure(string Label, string Value, UsageLevel Level);
