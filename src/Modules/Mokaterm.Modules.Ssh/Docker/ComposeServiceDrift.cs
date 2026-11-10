namespace Mokaterm.Modules.Ssh.Docker;

internal sealed record ComposeServiceDrift(string Service, ComposeDriftState State, string Detail);
