namespace Mokaterm.Abstractions.Containers;

/// <summary>The compose a Docker manager uses on its server, which every <see cref="DockerCommandProgram.Compose"/> command runs.</summary>
/// <param name="Command">How it is typed: <c>docker compose</c> or <c>docker-compose</c>.</param>
/// <param name="Version">What it reports, such as <c>2.29.1</c>.</param>
/// <param name="IsPlugin">The CLI plugin rather than the standalone program; version 1 of the standalone one has no <c>ls</c> and no JSON output.</param>
public sealed record DockerComposeInfo(string Command, string Version, bool IsPlugin);
