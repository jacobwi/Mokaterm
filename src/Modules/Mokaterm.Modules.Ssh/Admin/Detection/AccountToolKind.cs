namespace Mokaterm.Modules.Ssh.Admin.Detection;

/// <summary>Which tools add, change and remove accounts.</summary>
internal enum AccountToolKind
{
	None,

	/// <summary>shadow-utils: useradd, usermod, userdel.</summary>
	Shadow,

	/// <summary>BusyBox: adduser, addgroup, deluser, passwd -l.</summary>
	BusyBox,
}
