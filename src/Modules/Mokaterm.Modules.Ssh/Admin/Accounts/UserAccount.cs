namespace Mokaterm.Modules.Ssh.Admin.Accounts;

/// <summary>One passwd entry, with its groups and what the shadow file says about its password.</summary>
internal sealed record UserAccount
{
	public required string Name { get; init; }

	public long Uid { get; init; }

	public long Gid { get; init; }

	/// <summary>The GECOS field's first part, usually the full name.</summary>
	public string Comment { get; init; } = "";

	public string Home { get; init; } = "";

	public string Shell { get; init; } = "";

	public PasswordState Password { get; init; }

	/// <summary>The primary group's name, then every group that lists this user as a member.</summary>
	public IReadOnlyList<string> Groups { get; init; } = [];

	/// <summary>A shell such as nologin or false, so nobody logs in as this user interactively.</summary>
	public bool HasNoLogin => Shell.EndsWith("/nologin", StringComparison.Ordinal) || Shell.EndsWith("/false", StringComparison.Ordinal) || Shell.Length == 0;
}
