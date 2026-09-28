using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Modules.Ssh.FileSystem;

namespace Mokaterm.Modules.Ssh.Tests;

public sealed class AccountDirectoryTests
{
	private const string Passwd = """
		root:x:0:0:root:/root:/bin/bash
		daemon:x:1:1:daemon:/usr/sbin:/usr/sbin/nologin
		www-data:x:33:33:www-data:/var/www:/usr/sbin/nologin
		abc:x:1000:1000:A B C,,,:/home/abc:/bin/bash
		toor:x:0:0:duplicate root:/root:/bin/sh
		broken line without fields
		nobody:x:notanumber:65534::/:/bin/false
		""";

	private const string Group = "root:x:0:\r\nadm:x:4:syslog,abc\r\nwww-data:x:33:\r\nabc:x:1000:\r\n";

	[Fact]
	public void Parse_MapsIdsToNamesAndBack()
	{
		AccountDirectory accounts = AccountDirectory.Parse(Passwd, Group);

		Assert.Equal("www-data", accounts.UserName(33));
		Assert.Equal("abc", accounts.UserName(1000));
		Assert.Equal("adm", accounts.GroupName(4));
		Assert.Equal(33, accounts.UserId("www-data"));
		Assert.Equal(4, accounts.GroupId("adm"));
		Assert.False(accounts.IsEmpty);
	}

	[Fact]
	public void Parse_FirstEntryForAnIdWins() =>
		Assert.Equal("root", AccountDirectory.Parse(Passwd, Group).UserName(0));

	[Fact]
	public void UnknownIds_FallBackToNumbers()
	{
		AccountDirectory accounts = AccountDirectory.Parse(Passwd, Group);

		Assert.Equal("4242", accounts.UserName(4242));
		Assert.Equal("65534", accounts.GroupName(65534));
		Assert.Null(accounts.UserId("nobody"));
	}

	[Fact]
	public void Accounts_ListEachNameOnceInDatabaseOrder()
	{
		AccountDirectory accounts = AccountDirectory.Parse(Passwd, Group);

		Assert.Equal(
			[("root", 0L), ("daemon", 1L), ("www-data", 33L), ("abc", 1000L), ("toor", 0L)],
			accounts.Accounts.Users.Select(account => (account.Name, account.Id)));
		Assert.Equal(["root", "adm", "www-data", "abc"], accounts.Accounts.Groups.Select(account => account.Name));
	}

	[Fact]
	public void SecondNameForAnId_StillResolves()
	{
		AccountDirectory accounts = AccountDirectory.Parse(Passwd, Group);

		Assert.Equal(0, accounts.UserId("toor"));
		Assert.Equal("root", accounts.UserName(0));
	}

	[Fact]
	public void DuplicateName_KeepsItsFirstId()
	{
		AccountDirectory accounts = AccountDirectory.Parse("app:x:500:500::/:/bin/sh\napp:x:600:600::/:/bin/sh\n", "");

		RemoteAccount app = Assert.Single(accounts.Accounts.Users);
		Assert.Equal(500, app.Id);
		Assert.Equal(500, accounts.UserId("app"));
	}

	[Fact]
	public void NumericNames_ResolveToThemselves()
	{
		Assert.Equal(1234, AccountDirectory.Empty.UserId("1234"));
		Assert.Equal(55, AccountDirectory.Empty.GroupId("55"));
		Assert.Null(AccountDirectory.Empty.UserId("-1"));
		Assert.True(AccountDirectory.Empty.IsEmpty);
		Assert.True(AccountDirectory.Empty.Accounts.IsEmpty);
	}
}
