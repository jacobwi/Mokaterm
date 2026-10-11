namespace Mokaterm.Modules.Ssh.Admin.Detection;

/// <summary>What starts and stops services.</summary>
internal enum InitSystem
{
	/// <summary>Nothing recognisable runs as PID 1, which is usual in a container.</summary>
	None,

	Systemd,
	OpenRc,

	/// <summary>Scripts in <c>/etc/init.d</c> run by sysvinit.</summary>
	SysV,

	/// <summary>BusyBox init, with whatever scripts the image put in <c>/etc/init.d</c>.</summary>
	BusyBox,
}
