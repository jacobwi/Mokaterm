using Mokaterm.Modules.Ssh.Docker;

namespace Mokaterm.Modules.Ssh.Components.Docker;

/// <summary>A change ready to run: the call, what it is called, and the question asked first when it is destructive.</summary>
internal sealed record DockerChangePlan(DockerCall Call, string Title, string? Confirm = null, string ConfirmText = "Run", bool Destructive = true);
