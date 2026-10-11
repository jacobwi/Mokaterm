namespace Mokaterm.Abstractions.Sessions;

/// <summary>One fact the shell shows beside a session.</summary>
/// <param name="Label">A word or two in capitals, such as <c>AGENT</c> or <c>X11</c>.</param>
/// <param name="State">How it is drawn.</param>
/// <param name="Description">The tooltip: what it means right now. Holds no secret.</param>
public sealed record SessionIndicator(string Label, SessionIndicatorState State, string Description);
