namespace Mokaterm.DevHost.Demo.Docker;

/// <summary>
/// A demo command that prints as it goes, the way a build or a push does: its lines with the pause before each, then
/// what it leaves behind once it ran to its end. A cancel between two lines leaves nothing behind.
/// </summary>
/// <param name="Steps">Each line with the pause before it and whether it goes to stderr.</param>
/// <param name="Finish">Runs once the last line went out and makes the change (a new image, a pushed tag), returning the exit.</param>
internal sealed record DemoDockerStream(IReadOnlyList<DemoDockerLine> Steps, Func<int> Finish);
