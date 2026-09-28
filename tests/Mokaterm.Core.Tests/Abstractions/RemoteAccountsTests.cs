using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.Core.Tests.Abstractions;

public sealed class RemoteAccountsTests
{
	[Theory]
	[InlineData("www-data", true)]
	[InlineData("_apt", true)]
	[InlineData("1000", true)]
	[InlineData("jose.garcia", true)]
	[InlineData("", false)]
	[InlineData(null, false)]
	[InlineData("two words", false)]
	[InlineData("owner:group", false)]
	[InlineData("a/b", false)]
	[InlineData("tab\tname", false)]
	[InlineData("line\nbreak", false)]
	public void IsValidName_RejectsWhatChownCannotTake(string? name, bool expected) =>
		Assert.Equal(expected, RemoteAccount.IsValidName(name));

	[Fact]
	public void Empty_HasNoUsersOrGroups()
	{
		Assert.True(RemoteAccounts.Empty.IsEmpty);
		Assert.False(new RemoteAccounts { Groups = [new RemoteAccount("staff", 50)] }.IsEmpty);
	}
}
