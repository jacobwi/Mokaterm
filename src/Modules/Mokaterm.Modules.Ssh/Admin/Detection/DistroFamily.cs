namespace Mokaterm.Modules.Ssh.Admin.Detection;

/// <summary>The distribution family, from <c>ID</c> and <c>ID_LIKE</c>. It decides which package manager is expected.</summary>
internal enum DistroFamily
{
	Unknown,
	Debian,

	/// <summary>RHEL and everything built like it: Fedora, CentOS, Rocky, Alma, Oracle, Amazon Linux.</summary>
	RedHat,

	Suse,
	Arch,
	Alpine,
}
