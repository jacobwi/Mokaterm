namespace Mokaterm.Modules.Ssh.Admin.Services;

internal enum ServiceAction
{
	Start,
	Stop,
	Restart,

	/// <summary>Start at boot. It does not start the service now.</summary>
	Enable,

	/// <summary>Stop starting at boot. It does not stop the service now.</summary>
	Disable,
}
