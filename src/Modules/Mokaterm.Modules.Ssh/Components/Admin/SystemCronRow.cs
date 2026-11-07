using Mokaterm.Modules.Ssh.Admin.Cron;

namespace Mokaterm.Modules.Ssh.Components.Admin;

internal sealed record SystemCronRow(string File, int Index, CrontabEntry Entry);
