using System.Globalization;
using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.Modules.Ssh.FileSystem;

/// <summary>User and group names by id, read from the server's passwd and group databases.</summary>
internal sealed class AccountDirectory
{
	private readonly Dictionary<int, string> _users;
	private readonly Dictionary<int, string> _groups;
	private readonly Dictionary<string, int> _userIds;
	private readonly Dictionary<string, int> _groupIds;

	private AccountDirectory(List<RemoteAccount> users, List<RemoteAccount> groups)
	{
		_users = NamesById(users);
		_groups = NamesById(groups);
		_userIds = IdsByName(users);
		_groupIds = IdsByName(groups);
		Accounts = new RemoteAccounts { Users = users, Groups = groups };
	}

	public static AccountDirectory Empty { get; } = new([], []);

	public bool IsEmpty => Accounts.IsEmpty;

	/// <summary>Every user and group name once, in database order, with the id its first entry gives it.</summary>
	public RemoteAccounts Accounts { get; }

	/// <summary>
	/// Parses <c>name:x:uid:...</c> passwd lines and <c>name:x:gid:...</c> group lines. The first entry for a name wins,
	/// and so does the first name for an id when ids are shown as names.
	/// </summary>
	public static AccountDirectory Parse(string passwd, string group) => new(ParseAccounts(passwd), ParseAccounts(group));

	/// <summary>The user name, or the id as text when the server has no name for it.</summary>
	public string UserName(int uid) => _users.TryGetValue(uid, out string? name) ? name : uid.ToString(CultureInfo.InvariantCulture);

	public string GroupName(int gid) => _groups.TryGetValue(gid, out string? name) ? name : gid.ToString(CultureInfo.InvariantCulture);

	/// <summary>The id for a name, or the value itself when it is numeric.</summary>
	public int? UserId(string name) => ResolveId(name, _userIds);

	public int? GroupId(string name) => ResolveId(name, _groupIds);

	private static int? ResolveId(string name, Dictionary<string, int> ids) =>
		ids.TryGetValue(name, out int id) ? id
		: int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out int numeric) ? numeric
		: null;

	private static List<RemoteAccount> ParseAccounts(string database)
	{
		List<RemoteAccount> accounts = [];
		HashSet<string> seen = new(StringComparer.Ordinal);
		foreach (string line in database.Split('\n'))
		{
			string[] fields = line.TrimEnd('\r').Split(':');
			if (fields.Length >= 3
				&& fields[0].Length > 0
				&& int.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out int id)
				&& seen.Add(fields[0]))
			{
				accounts.Add(new RemoteAccount(fields[0], id));
			}
		}

		return accounts;
	}

	private static Dictionary<int, string> NamesById(List<RemoteAccount> accounts)
	{
		Dictionary<int, string> names = [];
		foreach (RemoteAccount account in accounts)
		{
			names.TryAdd((int)account.Id, account.Name);
		}

		return names;
	}

	private static Dictionary<string, int> IdsByName(List<RemoteAccount> accounts)
	{
		Dictionary<string, int> ids = new(StringComparer.Ordinal);
		foreach (RemoteAccount account in accounts)
		{
			ids.Add(account.Name, (int)account.Id);
		}

		return ids;
	}
}
