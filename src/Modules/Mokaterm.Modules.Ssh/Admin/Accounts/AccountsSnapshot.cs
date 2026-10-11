namespace Mokaterm.Modules.Ssh.Admin.Accounts;

/// <summary>Users, groups and the shells an account may be given, as the users page reads them.</summary>
internal sealed record AccountsSnapshot
{
	public IReadOnlyList<UserAccount> Users { get; init; } = [];

	public IReadOnlyList<GroupAccount> Groups { get; init; } = [];

	/// <summary>From <c>/etc/shells</c>; the shells useradd accepts without complaint.</summary>
	public IReadOnlyList<string> Shells { get; init; } = [];

	/// <summary>The first uid handed to people rather than services, <c>UID_MIN</c> from login.defs (1000 by default).</summary>
	public int FirstPersonUid { get; init; } = 1000;

	/// <summary>True when the shadow file was read, so <see cref="UserAccount.Password"/> is known.</summary>
	public bool KnowsPasswords { get; init; }

	/// <summary>Whether a uid belongs to a person rather than a service: from <see cref="FirstPersonUid"/>, root, but not nobody.</summary>
	public bool IsPerson(UserAccount user)
	{
		ArgumentNullException.ThrowIfNull(user);
		return user.Uid == 0 || (user.Uid >= FirstPersonUid && user.Uid is not (65534 or 4294967294));
	}
}
