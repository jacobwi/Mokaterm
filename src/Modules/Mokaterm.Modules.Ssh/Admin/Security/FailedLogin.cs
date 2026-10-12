namespace Mokaterm.Modules.Ssh.Admin.Security;

/// <summary>One login sshd turned down, as its log or <c>lastb</c> recorded it.</summary>
internal sealed record FailedLogin
{
	/// <summary>When, as the log wrote it: an ISO timestamp from the journal, <c>Oct 11 07:55:01</c> from a syslog file.</summary>
	public string Time { get; init; } = "";

	/// <summary>The user name the client tried, which for a scan is often one that does not exist.</summary>
	public required string User { get; init; }

	/// <summary>Where the attempt came from.</summary>
	public required string Address { get; init; }

	public int? Port { get; init; }

	/// <summary>What failed: <c>password</c>, <c>publickey</c>, <c>keyboard-interactive/pam</c>; empty when the log only says the user does not exist.</summary>
	public string Method { get; init; } = "";

	/// <summary>The user name does not exist on this machine.</summary>
	public bool InvalidUser { get; init; }
}
