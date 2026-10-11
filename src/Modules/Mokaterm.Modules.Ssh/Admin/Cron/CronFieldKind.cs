namespace Mokaterm.Modules.Ssh.Admin.Cron;

/// <summary>The five time fields of a crontab line, in their order on the line.</summary>
internal enum CronFieldKind
{
	Minute,
	Hour,
	DayOfMonth,
	Month,
	DayOfWeek,
}
