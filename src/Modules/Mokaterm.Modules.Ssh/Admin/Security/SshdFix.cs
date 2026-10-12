namespace Mokaterm.Modules.Ssh.Admin.Security;

/// <summary>
/// A change to one sshd setting that the security page offers for a check: the keyword as sshd spells it, the value
/// to set, the values <c>sshd -T</c> may print back for it, and whether it would refuse this session's next login.
/// </summary>
internal sealed record SshdFix
{
	/// <summary>The keyword as written in a configuration file, such as <c>PasswordAuthentication</c>.</summary>
	public required string Keyword { get; init; }

	public required string Value { get; init; }

	/// <summary>
	/// What <c>sshd -T</c> prints for the new value, lower case. Usually just <see cref="Value"/>; sshd prints
	/// <c>prohibit-password</c> as <c>without-password</c>.
	/// </summary>
	public required IReadOnlyList<string> Accepts { get; init; }

	/// <summary>The button, such as "Turn password logins off".</summary>
	public required string Label { get; init; }

	/// <summary>The next login the way this session logged in would be refused.</summary>
	public bool CutsConnection { get; init; }

	/// <summary>What the confirmation says in red or amber, or null.</summary>
	public string? Warning { get; init; }
}
