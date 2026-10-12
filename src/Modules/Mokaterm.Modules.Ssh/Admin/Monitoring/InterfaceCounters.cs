namespace Mokaterm.Modules.Ssh.Admin.Monitoring;

/// <summary>Bytes one network interface received and sent since it came up, from <c>/proc/net/dev</c>.</summary>
/// <param name="IsDevice">
/// True for an interface backed by hardware (or a VM's virtual NIC). Bridges, veth pairs and tunnels carry traffic that
/// also crosses a device, so the machine's total counts devices only when it knows any.
/// </param>
internal sealed record InterfaceCounters(string Name, long ReceivedBytes, long SentBytes, bool IsDevice);
