using Mokaterm.Abstractions.FileSystem;
using Mokaterm.UI.FileBrowser.Properties;
using Mokaterm.UI.FileBrowser.Tests.Fakes;
using static Mokaterm.UI.FileBrowser.Tests.Fakes.FakeFileSystem;

namespace Mokaterm.UI.FileBrowser.Tests;

public sealed class PropertiesChangeTests
{
	[Fact]
	public async Task Apply_ChangesOwnersBeforePermissions_RecursesOnlyIntoFolders_AndSkipsLinks()
	{
		FakeFileSystem fileSystem = new();
		PropertiesChange change = new()
		{
			Owner = "www-data",
			Group = "www",
			OwnershipRecursive = true,
			Permissions = new PermissionChange { Mode = Mode("755"), Recursive = true },
		};

		await change.ApplyAsync(
			fileSystem,
			[Folder("/srv/site"), File("/srv/index.html"), Link("/srv/current")],
			TestContext.Current.CancellationToken);

		Assert.Equal(
			[
				"chown -R www-data:www /srv/site",
				"chmod -R 755/7777 /srv/site",
				"chown www-data:www /srv/index.html",
				"chmod 755/7777 /srv/index.html",
			],
			fileSystem.Calls);
	}

	[Fact]
	public async Task Apply_GroupAlone_KeepsTheOwners()
	{
		FakeFileSystem fileSystem = new();
		PropertiesChange change = new() { Group = "staff", OwnershipRecursive = true };

		await change.ApplyAsync(fileSystem, [Folder("/srv/site")], TestContext.Current.CancellationToken);

		Assert.Equal(["chgrp -R staff /srv/site"], fileSystem.Calls);
	}

	[Fact]
	public async Task Apply_PassesTargetsAndConditionalExecuteThrough()
	{
		FakeFileSystem fileSystem = new();
		PropertiesChange change = new()
		{
			Permissions = new PermissionChange
			{
				Mode = Mode("020"),
				Mask = UnixFileMode.GroupWrite,
				Recursive = true,
				Targets = PermissionTargets.Folders,
				ConditionalExecute = true,
			},
		};

		await change.ApplyAsync(fileSystem, [Folder("/srv/site")], TestContext.Current.CancellationToken);

		Assert.Equal(["chmod -R 020/020 folders X /srv/site"], fileSystem.Calls);
	}

	[Fact]
	public async Task Apply_StopsWhenCancelled()
	{
		FakeFileSystem fileSystem = new();
		using CancellationTokenSource cancelled = new();
		await cancelled.CancelAsync();

		await Assert.ThrowsAsync<OperationCanceledException>(() =>
			new PropertiesChange { Owner = "root" }.ApplyAsync(fileSystem, [File("/srv/a.txt")], cancelled.Token));
		Assert.Empty(fileSystem.Calls);
	}

	[Fact]
	public void Action_NamesWhatChanges()
	{
		PermissionChange permissions = new() { Mode = Mode("644") };

		Assert.Equal("change the owner of", new PropertiesChange { Owner = "root", Group = "root" }.Action);
		Assert.Equal("change the group of", new PropertiesChange { Group = "staff" }.Action);
		Assert.Equal("change the permissions of", new PropertiesChange { Permissions = permissions }.Action);
		Assert.Equal("change the group and permissions of", new PropertiesChange { Group = "staff", Permissions = permissions }.Action);
	}
}
