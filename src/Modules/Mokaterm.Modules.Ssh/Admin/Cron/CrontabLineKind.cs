namespace Mokaterm.Modules.Ssh.Admin.Cron;

internal enum CrontabLineKind
{
	Blank,
	Comment,

	/// <summary><c>NAME=value</c>, such as <c>MAILTO</c> or <c>PATH</c>.</summary>
	Variable,

	Entry,

	/// <summary>Something cron would refuse. Kept as written, so saving never loses it.</summary>
	Invalid,
}
