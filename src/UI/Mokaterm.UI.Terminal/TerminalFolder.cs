namespace Mokaterm.UI.Terminal;

/// <summary>
/// Where the shell in a terminal says it is. <see cref="Path"/> is absolute when it came from OSC 7; from a prompt it is
/// whatever the prompt shows, which may start with <c>~</c> for the home of <see cref="User"/>, the prompt's user. OSC 7
/// names no user, so <see cref="User"/> is null then.
/// </summary>
public sealed record TerminalFolder(string Path, string? User);
