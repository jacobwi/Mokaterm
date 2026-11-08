namespace Mokaterm.Modules.Ssh.Docker;

// Bytes a second, each null until there are two samples to measure between.
internal readonly record struct DockerRates(double? Received, double? Sent, double? Read, double? Written);
