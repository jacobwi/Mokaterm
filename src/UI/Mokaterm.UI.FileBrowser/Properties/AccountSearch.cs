using System.Globalization;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.UI.FileBrowser.Browsing;

namespace Mokaterm.UI.FileBrowser.Properties;

/// <summary>Finds users and groups by what is typed into an owner or group field.</summary>
internal static class AccountSearch
{
	/// <summary>
	/// The accounts matching <paramref name="text"/>, best first: a name or id equal to it, then names starting with it,
	/// then names containing it, each group in name order. Empty text matches every account.
	/// </summary>
	public static List<RemoteAccount> Find(IReadOnlyList<RemoteAccount> accounts, string text)
	{
		ArgumentNullException.ThrowIfNull(accounts);
		string needle = text.Trim();
		List<(int Rank, RemoteAccount Account)> matches = [];
		foreach (RemoteAccount account in accounts)
		{
			if (Rank(account, needle) is int rank and >= 0)
			{
				matches.Add((rank, account));
			}
		}

		matches.Sort(static (a, b) => a.Rank != b.Rank ? a.Rank.CompareTo(b.Rank) : EntryOrdering.CompareNames(a.Account.Name, b.Account.Name));
		return [.. matches.Select(match => match.Account)];
	}

	/// <summary>The account named exactly <paramref name="text"/>, or the one with that id when the text is a number.</summary>
	public static RemoteAccount? Resolve(IReadOnlyList<RemoteAccount> accounts, string text)
	{
		ArgumentNullException.ThrowIfNull(accounts);
		string needle = text.Trim();
		RemoteAccount? byName = accounts.FirstOrDefault(account => string.Equals(account.Name, needle, StringComparison.Ordinal));
		if (byName is not null || !TryParseId(needle, out long id))
		{
			return byName;
		}

		return accounts.FirstOrDefault(account => account.Id == id);
	}

	public static bool TryParseId(string text, out long id) =>
		long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out id);

	private static int Rank(RemoteAccount account, string needle)
	{
		if (needle.Length == 0)
		{
			return 1;
		}

		if (account.Name.Equals(needle, StringComparison.OrdinalIgnoreCase)
			|| (TryParseId(needle, out long id) && account.Id == id))
		{
			return 0;
		}

		return account.Name.StartsWith(needle, StringComparison.OrdinalIgnoreCase) ? 1
			: account.Name.Contains(needle, StringComparison.OrdinalIgnoreCase) ? 2
			: -1;
	}
}
