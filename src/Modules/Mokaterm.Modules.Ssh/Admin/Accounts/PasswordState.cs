namespace Mokaterm.Modules.Ssh.Admin.Accounts;

/// <summary>What the password field of <c>/etc/shadow</c> starts with, which is all this view ever reads of it.</summary>
internal enum PasswordState
{
	/// <summary>Not read: the shadow file needs root.</summary>
	Unknown,

	/// <summary>A hash is set.</summary>
	Set,

	/// <summary>Starts with <c>!</c>: locked, or never given a password.</summary>
	Locked,

	/// <summary><c>*</c>: no password login at all, the usual state of system accounts.</summary>
	Disabled,

	/// <summary>Empty: logs in without a password wherever PAM allows that.</summary>
	Empty,
}
