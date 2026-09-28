using Mokaterm.Abstractions.FileSystem;
using Mokaterm.UI.FileBrowser.Properties;
using Mokaterm.UI.FileBrowser.Tests.Fakes;
using static Mokaterm.UI.FileBrowser.Tests.Fakes.FakeFileSystem;

namespace Mokaterm.UI.FileBrowser.Tests;

public sealed class TreeSizeWalkerTests
{
	[Fact]
	public async Task CountsEverythingInside_WithoutFollowingLinks()
	{
		FakeFileSystem fileSystem = Tree();
		List<TreeSize> reports = [];

		TreeSize size = await TreeSizeWalker.MeasureAsync(fileSystem, [Folder("/srv/site")], reports.Add, 1000, TestContext.Current.CancellationToken);

		Assert.Equal(new TreeSize(Files: 3, Folders: 1, Links: 1, Bytes: 7144, UnreadableFolders: 0, Truncated: false), size);
		Assert.Equal(["list /srv/site", "list /srv/site/img"], fileSystem.Calls);
		Assert.Equal(2, reports.Count);
		Assert.Equal(size, reports[^1]);
	}

	[Fact]
	public async Task SelectedFiles_CountTowardTheTotal()
	{
		TreeSize size = await TreeSizeWalker.MeasureAsync(
			Tree(),
			[Folder("/srv/site"), File("/srv/notes.txt", size: 10), Link("/srv/latest")],
			progress: null,
			1000,
			TestContext.Current.CancellationToken);

		Assert.Equal(4, size.Files);
		Assert.Equal(2, size.Links);
		Assert.Equal(7154, size.Bytes);
	}

	[Fact]
	public async Task UnreadableFolders_AreCountedAndSkipped()
	{
		FakeFileSystem fileSystem = Tree();
		fileSystem.Denied.Add("/srv/site/img");

		TreeSize size = await TreeSizeWalker.MeasureAsync(fileSystem, [Folder("/srv/site")], progress: null, 1000, TestContext.Current.CancellationToken);

		Assert.Equal(1, size.UnreadableFolders);
		Assert.Equal(1, size.Folders);
		Assert.Equal(1, size.Files);
		Assert.Equal(1000, size.Bytes);
		Assert.False(size.Truncated);
	}

	[Fact]
	public async Task Limit_StopsTheWalkEarly()
	{
		FakeFileSystem fileSystem = Tree();

		TreeSize size = await TreeSizeWalker.MeasureAsync(fileSystem, [Folder("/srv/site")], progress: null, limit: 2, TestContext.Current.CancellationToken);

		Assert.True(size.Truncated);
		Assert.Equal(2, size.Items);
		Assert.DoesNotContain("list /srv/site/img", fileSystem.Calls);
	}

	[Fact]
	public async Task Cancellation_StopsTheWalk()
	{
		FakeFileSystem fileSystem = Tree();
		using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
		fileSystem.BeforeList = async (path, _) =>
		{
			if (path == "/srv/site/img")
			{
				await stop.CancelAsync();
			}
		};

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
			TreeSizeWalker.MeasureAsync(fileSystem, [Folder("/srv/site")], progress: null, 1000, stop.Token));
	}

	[Fact]
	public async Task LostConnection_EndsTheWalk()
	{
		FakeFileSystem fileSystem = Tree();
		fileSystem.BeforeList = (path, _) => path == "/srv/site/img"
			? Task.FromException(new RemoteFileSystemException(RemoteFileErrorKind.ConnectionLost, "The connection was lost.", path))
			: Task.CompletedTask;

		RemoteFileSystemException failure = await Assert.ThrowsAsync<RemoteFileSystemException>(() =>
			TreeSizeWalker.MeasureAsync(fileSystem, [Folder("/srv/site")], progress: null, 1000, TestContext.Current.CancellationToken));

		Assert.Equal(RemoteFileErrorKind.ConnectionLost, failure.Kind);
	}

	private static FakeFileSystem Tree() => new FakeFileSystem()
		.Contains("/srv/site", File("/srv/site/index.html", size: 1000), Folder("/srv/site/img"), Link("/srv/site/current", RemoteEntryKind.Directory))
		.Contains("/srv/site/img", File("/srv/site/img/a.png", size: 2048), File("/srv/site/img/b.png", size: 4096));
}
