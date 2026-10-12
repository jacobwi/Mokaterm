namespace Mokaterm.Modules.Ssh.Admin.Monitoring;

/// <summary>What one interface received and sent per second between two samples.</summary>
internal sealed record InterfaceRate(string Name, double ReceivedPerSecond, double SentPerSecond, bool IsDevice);
