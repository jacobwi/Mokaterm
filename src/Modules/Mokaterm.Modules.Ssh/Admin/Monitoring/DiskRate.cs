namespace Mokaterm.Modules.Ssh.Admin.Monitoring;

/// <summary>Bytes one whole disk read and wrote per second between two samples.</summary>
internal sealed record DiskRate(string Name, double ReadPerSecond, double WrittenPerSecond);
