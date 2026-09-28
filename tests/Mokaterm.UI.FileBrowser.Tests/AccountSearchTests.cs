using Mokaterm.Abstractions.FileSystem;
using Mokaterm.UI.FileBrowser.Properties;

namespace Mokaterm.UI.FileBrowser.Tests;

public sealed class AccountSearchTests
{
	private static readonly RemoteAccount[] Users =
	[
		new("root", 0),
		new("daemon", 1),
		new("www-data", 33),
		new("backup", 34),
		new("abc", 1000),
		new("abcd", 1001),
	];

	[Fact]
	public void EmptyText_ListsEveryAccountByName() =>
		Assert.Equal(["abc", "abcd", "backup", "daemon", "root", "www-data"], Names(AccountSearch.Find(Users, " ")));

	[Fact]
	public void ExactMatchesComeFirst_ThenPrefixes_ThenContains()
	{
		Assert.Equal(["abc", "abcd", "backup", "daemon", "www-data"], Names(AccountSearch.Find(Users, "a")));
		Assert.Equal(["daemon", "abcd", "www-data"], Names(AccountSearch.Find(Users, "d")));
		Assert.Equal(["abc", "abcd"], Names(AccountSearch.Find(Users, "ABC")));
		Assert.Empty(AccountSearch.Find(Users, "zz"));
	}

	[Fact]
	public void Ids_FindTheirAccount() =>
		Assert.Equal(["www-data"], Names(AccountSearch.Find(Users, "33")));

	[Theory]
	[InlineData("www-data", "www-data")]
	[InlineData("33", "www-data")]
	[InlineData("0", "root")]
	[InlineData("WWW-DATA", null)]
	[InlineData("4242", null)]
	[InlineData("", null)]
	public void Resolve_MatchesANameExactlyOrAnId(string text, string? expected) =>
		Assert.Equal(expected, AccountSearch.Resolve(Users, text)?.Name);

	private static List<string> Names(List<RemoteAccount> accounts) => [.. accounts.Select(account => account.Name)];
}
