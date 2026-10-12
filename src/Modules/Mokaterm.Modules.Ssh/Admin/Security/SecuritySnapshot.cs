using Mokaterm.Modules.Ssh.Admin.Network;

namespace Mokaterm.Modules.Ssh.Admin.Security;

/// <summary>Everything the security page reads in one go: sshd's settings, failed logins, fail2ban and listening ports.</summary>
internal sealed record SecuritySnapshot
{
	/// <summary>The read ran as root, so sshd -T, every log and every port's process were open to it.</summary>
	public bool AsRoot { get; init; }

	public SshdConfig Sshd { get; init; } = SshdConfig.None;

	/// <summary>Recent failed SSH logins, oldest first, at most a few hundred.</summary>
	public IReadOnlyList<FailedLogin> FailedLogins { get; init; } = [];

	/// <summary>Where they were read: the journal, a log file's path, <c>lastb</c>, or empty when nothing could be read.</summary>
	public string FailedLoginSource { get; init; } = "";

	/// <summary>Null when fail2ban is not installed.</summary>
	public Fail2BanStatus? Fail2Ban { get; init; }

	public IReadOnlyList<ListeningSocket> Listening { get; init; } = [];

	/// <summary><c>ss</c>, <c>netstat</c>, or empty when neither is installed.</summary>
	public string ListeningSource { get; init; } = "";
}
