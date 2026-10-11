using Mokaterm.Modules.Ssh.Docker;

namespace Mokaterm.Modules.Ssh.Components.Docker;

/// <summary>A compose action ready to run: its commands in order, its title, and the question asked first, if any.</summary>
internal sealed record ComposeActionPlan(IReadOnlyList<DockerCall> Calls, string Title, string? Confirm = null, string ConfirmText = "Run");
