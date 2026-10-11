namespace Mokaterm.Modules.Ssh.Admin.Cron;

/// <summary>One line of a crontab as written, and what it was read as.</summary>
internal sealed record CrontabLine(CrontabLineKind Kind, string Text, CrontabEntry? Entry = null, string? Problem = null);
