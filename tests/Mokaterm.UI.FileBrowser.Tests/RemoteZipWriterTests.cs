using Mokaterm.Abstractions.FileSystem;
using Mokaterm.UI.FileBrowser.Tests.Fakes;
using Mokaterm.UI.FileBrowser.Transfers;
using static Mokaterm.UI.FileBrowser.Tests.Fakes.FakeFileSystem;

namespace Mokaterm.UI.FileBrowser.Tests;

public sealed class RemoteZipWriterTests
{
	// A backslash is an ordinary character in a POSIX name, but extractors on Windows split entry names on it, so a
	// server could name a file to land outside the folder the archive is extracted to.
	[Fact]
	public async Task Plan_BackslashesInServerNames_DoNotBecomeFolders()
	{
		FakeFileSystem fileSystem = new FakeFileSystem()
			.Contains("/srv/site", File(@"/srv/site/..\..\Startup\evil.bat", size: 10), Folder(@"/srv/site/sub\dir"))
			.Contains(@"/srv/site/sub\dir", File(@"/srv/site/sub\dir/page.html", size: 1));

		IReadOnlyList<ZipItem> plan = await RemoteZipWriter.PlanAsync(
			fileSystem,
			[Folder("/srv/site"), File(@"/srv/x\y.txt", size: 2)],
			TestContext.Current.CancellationToken);

		Assert.Equal(
			["site/", "site/.._.._Startup_evil.bat", "site/sub_dir/", "site/sub_dir/page.html", "x_y.txt"],
			plan.Select(item => item.EntryName));
	}

	[Fact]
	public async Task Plan_LinksToFolders_AreNotFollowed()
	{
		FakeFileSystem fileSystem = new FakeFileSystem()
			.Contains("/srv/site", File("/srv/site/index.html", size: 10), Link("/srv/site/loop", RemoteEntryKind.Directory));

		IReadOnlyList<ZipItem> plan = await RemoteZipWriter.PlanAsync(fileSystem, [Folder("/srv/site")], TestContext.Current.CancellationToken);

		Assert.Equal(["site/", "site/index.html"], plan.Select(item => item.EntryName));
		Assert.Equal(["list /srv/site"], fileSystem.Calls);
	}
}
