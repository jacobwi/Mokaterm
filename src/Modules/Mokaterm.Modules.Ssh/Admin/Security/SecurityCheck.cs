namespace Mokaterm.Modules.Ssh.Admin.Security;

/// <summary>One line of the security page: what was checked, how it came out, and what can be done about it here.</summary>
internal sealed record SecurityCheck
{
	/// <summary>Stable, such as <c>sshd.passwordauthentication</c>, so a selection survives a reload.</summary>
	public required string Id { get; init; }

	public required string Title { get; init; }

	/// <summary>The value as the machine has it, such as <c>yes</c> or <c>3 jails</c>.</summary>
	public string Value { get; init; } = "";

	public required SecurityLevel Level { get; init; }

	/// <summary>One sentence on what the value means.</summary>
	public required string Summary { get; init; }

	/// <summary>The sshd keyword behind the check, whose file the detail names and an edit opens; null for the others.</summary>
	public string? Keyword { get; init; }

	/// <summary>The change offered for it, or null when there is none to offer.</summary>
	public SshdFix? Fix { get; init; }

	/// <summary>
	/// Where the rest is shown: <c>logins</c>, <c>ports</c> or <c>fail2ban</c> on this page, <c>security-updates</c> or
	/// <c>packages</c> on the packages page.
	/// </summary>
	public string? Shows { get; init; }
}
