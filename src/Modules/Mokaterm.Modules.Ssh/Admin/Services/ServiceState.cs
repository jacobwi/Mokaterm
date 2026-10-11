namespace Mokaterm.Modules.Ssh.Admin.Services;

/// <summary>Where a service is, folded from what each init system prints, for the dot beside it.</summary>
internal enum ServiceState
{
	Unknown,
	Running,

	/// <summary>Active without a process, such as a oneshot that ran (<c>active (exited)</c>).</summary>
	Exited,

	Stopped,

	/// <summary>Starting, stopping, reloading.</summary>
	Changing,

	Failed,
}
