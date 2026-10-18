namespace Mokaterm.DevHost.Demo.Docker;

/// <summary>One line of a <see cref="DemoDockerStream"/>.</summary>
/// <param name="Pause">How long docker took before printing it.</param>
/// <param name="Text">The line without its line break.</param>
/// <param name="IsError">It goes to stderr.</param>
internal sealed record DemoDockerLine(TimeSpan Pause, string Text, bool IsError);
