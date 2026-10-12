namespace Mokaterm.UI.Common.ToolPages;

/// <summary>How a Docker manager page names a change and asks about it before it runs.</summary>
/// <param name="Title">What it is, for example "Stop web": the question's title and the outcome's heading.</param>
/// <param name="Confirm">The question for a change that needs one, or null to run at once. The question always shows the exact commands.</param>
/// <param name="ConfirmText">The confirm button, for example "Stop".</param>
/// <param name="Destructive">The question is styled as dangerous.</param>
public sealed record DockerChangePrompt(string Title, string? Confirm = null, string ConfirmText = "Run", bool Destructive = true);
