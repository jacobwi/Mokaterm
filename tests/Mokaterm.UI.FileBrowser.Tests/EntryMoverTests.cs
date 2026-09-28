using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.FileBrowser.Browsing;
using Mokaterm.UI.FileBrowser.Tests.Fakes;
using Mokaterm.UI.FileBrowser.Transfers;
using static Mokaterm.UI.FileBrowser.Tests.Fakes.FakeFileSystem;

namespace Mokaterm.UI.FileBrowser.Tests;

public sealed class EntryMoverTests
{
	// "lnk" points back at /srv/app, so "moving" app.log into it finds app.log already there: it is the same file, and
	// replacing it would have deleted it on a server without an atomic replace.
	[Theory]
	[InlineData(OverwriteBehavior.Overwrite)]
	[InlineData(OverwriteBehavior.Skip)]
	public async Task Move_ThroughALinkBackToTheSameFolder_LeavesTheEntryAlone(OverwriteBehavior behavior)
	{
		FakeFileSystem fileSystem = new FakeFileSystem()
			.Contains("/srv/app", File("/srv/app/app.log", size: 5), Link("/srv/app/lnk", RemoteEntryKind.Directory));
		fileSystem.FolderLinks["/srv/app/lnk"] = "/srv/app";

		await EntryMover.MoveAsync(fileSystem, [File("/srv/app/app.log", size: 5)], "/srv/app/lnk", Resolver(behavior), TestContext.Current.CancellationToken);

		Assert.Equal(["mv /srv/app/app.log /srv/app/lnk/app.log"], fileSystem.Calls);
	}

	[Fact]
	public async Task Move_NameTakenInAnotherFolder_IsReplacedWhenTheSettingsSaySo()
	{
		FakeFileSystem fileSystem = new FakeFileSystem()
			.Contains("/srv/app", File("/srv/app/app.log", size: 5), File("/srv/app/new.log", size: 1))
			.Contains("/srv/archive", File("/srv/archive/app.log", size: 9));

		await EntryMover.MoveAsync(
			fileSystem,
			[File("/srv/app/app.log", size: 5), File("/srv/app/new.log", size: 1)],
			"/srv/archive",
			Resolver(OverwriteBehavior.Overwrite),
			TestContext.Current.CancellationToken);

		Assert.Equal(
			["mv /srv/app/app.log /srv/archive/app.log", "mv -f /srv/app/app.log /srv/archive/app.log", "mv /srv/app/new.log /srv/archive/new.log"],
			fileSystem.Calls);
	}

	[Fact]
	public async Task Move_NameTakenWithSkip_KeepsBothAndMovesTheRest()
	{
		FakeFileSystem fileSystem = new FakeFileSystem()
			.Contains("/srv/app", File("/srv/app/app.log", size: 5), File("/srv/app/new.log", size: 1))
			.Contains("/srv/archive", File("/srv/archive/app.log", size: 9));

		await EntryMover.MoveAsync(
			fileSystem,
			[File("/srv/app/app.log", size: 5), File("/srv/app/new.log", size: 1)],
			"/srv/archive",
			Resolver(OverwriteBehavior.Skip),
			TestContext.Current.CancellationToken);

		Assert.Equal(["mv /srv/app/app.log /srv/archive/app.log", "mv /srv/app/new.log /srv/archive/new.log"], fileSystem.Calls);
	}

	private static OverwriteResolver Resolver(OverwriteBehavior behavior) => new(new ThrowingInteraction(), behavior, isBatch: true);
}
