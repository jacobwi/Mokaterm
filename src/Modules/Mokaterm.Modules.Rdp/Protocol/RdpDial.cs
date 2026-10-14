using System.Net;
using System.Net.Sockets;

namespace Mokaterm.Modules.Rdp.Protocol;

/// <summary>
/// Opens the TCP connection for an attempt. <see cref="RdpDialer.ConnectAsync"/> is the real one; tests hand in a dial
/// that never answers, which a loopback port cannot do, to see what a firewall that drops the packets looks like.
/// </summary>
internal delegate Task<Socket> RdpDial(string host, int port, TimeSpan budget, Action<IPAddress>? tried, CancellationToken cancellationToken);
