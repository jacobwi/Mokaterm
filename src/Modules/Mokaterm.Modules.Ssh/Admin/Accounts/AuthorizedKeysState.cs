namespace Mokaterm.Modules.Ssh.Admin.Accounts;

/// <summary>What reading a user's <c>~/.ssh/authorized_keys</c> found.</summary>
internal enum AuthorizedKeysState
{
	/// <summary>The file was read.</summary>
	Present,

	/// <summary>The folder is there and the file is not: no key logs in as this user.</summary>
	NoFile,

	/// <summary>There is no <c>~/.ssh</c> folder yet.</summary>
	NoFolder,

	/// <summary>The account has no home folder to hold one.</summary>
	NoHome,

	/// <summary>The home, the folder or the file cannot be read by whoever read it: another user's, without root.</summary>
	Unreadable,
}
