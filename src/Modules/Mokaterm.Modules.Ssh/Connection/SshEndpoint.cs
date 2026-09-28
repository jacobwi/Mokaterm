namespace Mokaterm.Modules.Ssh.Connection;

/// <summary>
/// Where a socket goes. It is the connection's own address for a direct session, and a loopback port on this machine
/// when the session reaches its target through a jump host.
/// </summary>
internal sealed record SshEndpoint(string Host, int Port);
