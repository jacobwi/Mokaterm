using System.Globalization;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Modules.Ssh.Admin.Detection;

namespace Mokaterm.Modules.Ssh.Admin.Accounts;

/// <summary>
/// Listing accounts, and changing them with shadow-utils or BusyBox's tools. A new password only ever goes to chpasswd
/// on stdin; the shadow file is read for the first character of each password field and never for a hash.
/// </summary>
internal sealed class AccountCommands
{
	// Groups that hand out sudo on the common distributions.
	private static readonly HashSet<string> AdminGroups = new(StringComparer.Ordinal) { "sudo", "wheel", "admin" };

	public AccountCommands(AccountToolKind tools, bool hasChpasswd)
	{
		Tools = tools;
		CanSetPasswords = hasChpasswd;
	}

	public AccountToolKind Tools { get; }

	/// <summary>Accounts can be added, locked and deleted.</summary>
	public bool CanChange => Tools != AccountToolKind.None;

	public bool CanSetPasswords { get; }

	/// <summary>Everything the users page shows. As root it also reads whether each password is set or locked.</summary>
	public static AdminScript Read(bool asRoot) => new()
	{
		Name = "accounts.read",
		AsRoot = asRoot,
		Body = string.Join('\n',
			AdminSections.Start("passwd"),
			"getent passwd 2>/dev/null || cat /etc/passwd",
			AdminSections.Start("group"),
			"getent group 2>/dev/null || cat /etc/group",
			AdminSections.Start("shells"),
			"cat /etc/shells 2>/dev/null",
			AdminSections.Start("logindefs"),
			"grep -E '^[[:space:]]*UID_MIN[[:space:]]' /etc/login.defs 2>/dev/null",
			"if [ -r /etc/shadow ]; then",
			$"\t{AdminSections.Start("shadow")}",

			// The first character says locked, disabled, empty or set; the hash itself never leaves the server.
			"\tawk -F: '{ print $1 \":\" substr($2, 1, 1) }' /etc/shadow",
			"fi",
			"exit 0"),
	};

	public static AccountsSnapshot Parse(string output)
	{
		AdminSections sections = AdminSections.Parse(output);
		List<GroupAccount> groups = [];
		foreach (string line in AdminSections.Lines(sections.Get("group")))
		{
			string[] fields = line.Split(':');
			if (fields.Length >= 3 && fields[0].Length > 0 && long.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out long gid))
			{
				string[] members = fields.Length > 3 ? fields[3].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [];
				groups.Add(new GroupAccount(fields[0], gid, members));
			}
		}

		Dictionary<string, PasswordState> passwords = new(StringComparer.Ordinal);
		string? shadow = sections.Get("shadow");
		foreach (string line in AdminSections.Lines(shadow))
		{
			int colon = line.IndexOf(':', StringComparison.Ordinal);
			if (colon > 0)
			{
				passwords[line[..colon]] = (colon + 1 < line.Length ? line[colon + 1] : '\0') switch
				{
					'\0' => PasswordState.Empty,
					'!' => PasswordState.Locked,
					'*' => PasswordState.Disabled,
					_ => PasswordState.Set,
				};
			}
		}

		Dictionary<long, string> groupNames = [];
		foreach (GroupAccount group in groups)
		{
			groupNames.TryAdd(group.Gid, group.Name);
		}

		List<UserAccount> users = [];
		foreach (string line in AdminSections.Lines(sections.Get("passwd")))
		{
			string[] fields = line.Split(':');
			if (fields.Length < 7 || fields[0].Length == 0
				|| !long.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out long uid)
				|| !long.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out long gid))
			{
				continue;
			}

			string name = fields[0];
			List<string> memberOf = groupNames.TryGetValue(gid, out string? primary) ? [primary] : [];
			memberOf.AddRange(groups.Where(group => group.Members.Contains(name) && group.Name != primary).Select(group => group.Name));
			users.Add(new UserAccount
			{
				Name = name,
				Uid = uid,
				Gid = gid,
				Comment = fields[4].Split(',')[0],
				Home = fields[5],
				Shell = fields[6],
				Password = passwords.TryGetValue(name, out PasswordState state) ? state : PasswordState.Unknown,
				Groups = memberOf,
			});
		}

		int firstPersonUid = 1000;
		foreach (string line in AdminSections.Lines(sections.Get("logindefs")))
		{
			string[] words = line.Trim().Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
			if (words is ["UID_MIN", var value] && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed))
			{
				firstPersonUid = parsed;
			}
		}

		return new AccountsSnapshot
		{
			Users = [.. users.OrderBy(user => user.Uid).ThenBy(user => user.Name, StringComparer.Ordinal)],
			Groups = [.. groups.OrderBy(group => group.Name, StringComparer.Ordinal)],
			Shells = [.. AdminSections.Lines(sections.Get("shells")).Select(line => line.Trim()).Where(line => line.StartsWith('/') && AdminNames.CheckShellPath(line) is null).Distinct(StringComparer.Ordinal)],
			FirstPersonUid = firstPersonUid,
			KnowsPasswords = shadow is not null,
		};
	}

	/// <summary>
	/// A new account with a home folder (unless <paramref name="createHome"/> is off) and the given shell. With a
	/// password, chpasswd reads <c>name:password</c> from stdin; without one the account cannot log in by password.
	/// </summary>
	public (AdminAction Action, string? Input) AddUser(string name, string? comment, string shell, bool createHome, string? password)
	{
		RequireChange();
		Require(AdminNames.CheckAccount(name), nameof(name));
		Require(AdminNames.CheckComment(comment), nameof(comment));
		Require(AdminNames.CheckShellPath(shell), nameof(shell));
		bool withPassword = !string.IsNullOrEmpty(password);
		if (withPassword)
		{
			Require(AdminNames.CheckPassword(password), nameof(password));
			if (!CanSetPasswords)
			{
				throw new InvalidOperationException("This machine has no chpasswd.");
			}
		}

		string quoted = PosixShell.Quote(name);
		string gecos = string.IsNullOrEmpty(comment) ? "" : Tools == AccountToolKind.Shadow ? $" -c {PosixShell.Quote(comment)}" : $" -g {PosixShell.Quote(comment)}";
		string create = Tools == AccountToolKind.Shadow
			? $"useradd {(createHome ? "-m" : "-M")} -s {PosixShell.Quote(shell)}{gecos} -- {quoted}"
			: $"adduser -D{(createHome ? "" : " -H")} -s {PosixShell.Quote(shell)}{gecos} {quoted}";

		return (new AdminAction
		{
			Title = $"Add user {name}",
			Script = Root("accounts.add", withPassword ? $"set -e\n{create}\nchpasswd" : create, withPassword),
			Description = withPassword
				? "Creates the account, then sets its password through chpasswd, which reads it from stdin."
				: "Creates the account without a password, so it cannot log in with one until a password is set.",
			ConfirmText = "Add user",
		}, withPassword ? $"{name}:{password}" : null);
	}

	public (AdminAction Action, string Input) SetPassword(string name, string password)
	{
		Require(AdminNames.CheckAccount(name), nameof(name));
		Require(AdminNames.CheckPassword(password), nameof(password));
		if (!CanSetPasswords)
		{
			throw new InvalidOperationException("This machine has no chpasswd.");
		}

		return (new AdminAction
		{
			Title = $"Set the password of {name}",
			Script = Root("accounts.password", "chpasswd", readsInput: true),
			Description = "chpasswd reads the new password from stdin, so it never appears in a command line or a process list.",
			ConfirmText = "Set password",
		}, $"{name}:{password}");
	}

	public AdminAction Lock(string name, bool locked)
	{
		RequireChange();
		Require(AdminNames.CheckAccount(name), nameof(name));
		string quoted = PosixShell.Quote(name);
		string body = Tools == AccountToolKind.Shadow
			? $"usermod {(locked ? "-L" : "-U")} -- {quoted}"
			: $"passwd {(locked ? "-l" : "-u")} {quoted}";

		return new AdminAction
		{
			Title = locked ? $"Lock {name}" : $"Unlock {name}",
			Script = Root(locked ? "accounts.lock" : "accounts.unlock", body),
			Description = locked
				? "Locks the password, so password logins fail. Logins with an SSH key keep working; remove the user's keys or delete the account to stop those."
				: "Unlocks the password. An account that never had one stays without one, and usermod refuses to unlock it.",
			Destructive = locked,
			ConfirmText = locked ? "Lock" : "Unlock",
		};
	}

	public AdminAction AddToGroup(string user, string group)
	{
		RequireChange();
		Require(AdminNames.CheckAccount(user), nameof(user));
		Require(AdminNames.CheckAccount(group), nameof(group));
		string body = Tools == AccountToolKind.Shadow
			? $"usermod -a -G {PosixShell.Quote(group)} -- {PosixShell.Quote(user)}"
			: $"addgroup {PosixShell.Quote(user)} {PosixShell.Quote(group)}";

		return new AdminAction
		{
			Title = $"Add {user} to {group}",
			Script = Root("accounts.group", body),
			Description = "Takes effect at the user's next login; sessions already open keep their old groups.",
			Warning = group is "sudo" or "wheel" or "admin" or "root" ? $"Members of {group} can usually run anything as root." : null,
			ConfirmText = "Add to group",
		};
	}

	/// <summary>Deletes an account. Root and the account this session logged in with are refused before any script is built.</summary>
	public AdminAction DeleteUser(UserAccount user, bool removeHome, string loginName, int firstPersonUid = 1000)
	{
		RequireChange();
		ArgumentNullException.ThrowIfNull(user);
		Require(AdminNames.CheckAccount(user.Name), nameof(user));
		if (WhyNotDelete(user, loginName) is { } refusal)
		{
			throw new InvalidOperationException(refusal);
		}

		string quoted = PosixShell.Quote(user.Name);
		string body = Tools == AccountToolKind.Shadow
			? $"userdel{(removeHome ? " -r" : "")} -- {quoted}"
			: $"deluser{(removeHome ? " --remove-home" : "")} {quoted}";

		return new AdminAction
		{
			Title = $"Delete user {user.Name}",
			Script = Root("accounts.delete", body),
			Description = removeHome
				? $"Deletes the account, its home folder {user.Home} and its mail spool. Files it owns elsewhere stay, owned by a number."
				: "Deletes the account. Its home folder and files stay, owned by a number that a new account may get later.",
			Warning = user.Uid < firstPersonUid ? "This looks like a system account. Services that run as it stop working." : null,
			Destructive = true,
			ConfirmText = "Delete user",
		};
	}

	/// <summary>A new group with no members: groupadd, or addgroup on BusyBox.</summary>
	public AdminAction AddGroup(string name)
	{
		RequireChange();
		Require(AdminNames.CheckAccount(name), nameof(name));
		string quoted = PosixShell.Quote(name);
		return new AdminAction
		{
			Title = $"Add group {name}",
			Script = Root("accounts.addgroup", Tools == AccountToolKind.Shadow ? $"groupadd -- {quoted}" : $"addgroup {quoted}"),
			Description = "Creates the group with the next free group id and no members.",
			ConfirmText = "Add group",
		};
	}

	/// <summary>
	/// Deletes a group: groupdel, or delgroup on BusyBox. A group that is still someone's primary group, and the root
	/// group, are refused before any script is built.
	/// </summary>
	public AdminAction DeleteGroup(GroupAccount group, AccountsSnapshot snapshot, string loginName)
	{
		RequireChange();
		ArgumentNullException.ThrowIfNull(group);
		ArgumentNullException.ThrowIfNull(snapshot);
		Require(AdminNames.CheckAccount(group.Name), nameof(group));
		if (WhyNotDeleteGroup(group, snapshot) is { } refusal)
		{
			throw new InvalidOperationException(refusal);
		}

		bool sessionLosesRoot = AdminGroups.Contains(group.Name) && group.Members.Contains(loginName);
		string quoted = PosixShell.Quote(group.Name);
		return new AdminAction
		{
			Title = $"Delete group {group.Name}",
			Script = Root("accounts.delgroup", Tools == AccountToolKind.Shadow ? $"groupdel -- {quoted}" : $"delgroup {quoted}"),
			Description = group.Members.Count == 0
				? "Deletes the group. Files it owns stay, owned by a number that a new group may get later."
				: $"Deletes the group, and with it the membership of {string.Join(", ", group.Members)}. Files it owns stay, owned by a number.",
			Warning = sessionLosesRoot
				? $"{loginName}, the account this session logged in as, is in {group.Name}, which is what usually lets it use sudo. Without it this session's root mode, and the next sudo, are refused."
				: group.Gid < snapshot.FirstPersonUid ? "This looks like a system group. Services that rely on it may stop working." : null,
			CutsConnection = sessionLosesRoot,
			Destructive = true,
			ConfirmText = "Delete group",
		};
	}

	/// <summary>Why a group cannot be deleted here, or null: the root group, and a group that is a user's primary group.</summary>
	public static string? WhyNotDeleteGroup(GroupAccount group, AccountsSnapshot snapshot)
	{
		ArgumentNullException.ThrowIfNull(group);
		ArgumentNullException.ThrowIfNull(snapshot);
		if (group.Gid == 0)
		{
			return "The root group cannot be deleted here.";
		}

		return snapshot.Users.FirstOrDefault(user => user.Gid == group.Gid) is { } owner
			? $"It is the primary group of {owner.Name}, so groupdel refuses it while that account exists."
			: null;
	}

	/// <summary>Why an account cannot be deleted here, or null.</summary>
	public static string? WhyNotDelete(UserAccount user, string loginName)
	{
		ArgumentNullException.ThrowIfNull(user);
		if (user.Uid == 0)
		{
			return "Root cannot be deleted here.";
		}

		return string.Equals(user.Name, loginName, StringComparison.Ordinal) ? "This session is logged in as this user." : null;
	}

	private static AdminScript Root(string name, string body, bool readsInput = false) => new()
	{
		Name = name,
		Body = body,
		AsRoot = true,
		ReadsInput = readsInput,
		Timeout = AdminScript.ActionTimeout,
		OutputLimitBytes = AdminScript.ActionOutputBytes,
	};

	private void RequireChange()
	{
		if (!CanChange)
		{
			throw new InvalidOperationException("This machine has neither useradd nor adduser.");
		}
	}

	private static void Require(string? problem, string parameter)
	{
		if (problem is not null)
		{
			throw new ArgumentException(problem, parameter);
		}
	}
}
