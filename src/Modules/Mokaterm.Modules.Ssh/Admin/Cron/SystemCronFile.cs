namespace Mokaterm.Modules.Ssh.Admin.Cron;

/// <summary>One of the system's cron files, read only: <c>/etc/crontab</c> or a file in <c>/etc/cron.d</c>.</summary>
internal sealed record SystemCronFile(string Path, CrontabDocument? Document, bool Readable);
