namespace Mokaterm.Abstractions.Import;

/// <summary>How a source writes the path of a login's key file, which decides how it is turned into a file on this machine.</summary>
public enum IdentityPathStyle
{
	/// <summary>A full path as PuTTY and WinSCP keep it, with <c>%NAME%</c> environment variables.</summary>
	Plain,

	/// <summary>
	/// An OpenSSH <c>IdentityFile</c>: <c>~</c> for the home folder, <c>%</c> tokens such as <c>%h</c> and <c>%r</c>,
	/// <c>${NAME}</c> environment variables, and a relative path counted from <c>~/.ssh</c>.
	/// </summary>
	OpenSsh,
}
