using Mokaterm.Abstractions.FileSystem;
using Mokaterm.UI.FileBrowser.Search;

namespace Mokaterm.UI.FileBrowser.Tests;

public sealed class WalkStatusTests
{
	private static readonly RemoteSearchQuery Query = new() { Pattern = "*.log", MaxResults = 50, MaxFolders = 200, MaxDepth = 8 };

	[Fact]
	public void Running_NamesTheFolderBeingRead()
	{
		Assert.Equal("Searching /var/log...", WalkStatus.Running("Searching", "/var/log", progress: null));
		Assert.Equal(
			"Searching /var/log/nginx... 12 folders, 3 found",
			WalkStatus.Running("Searching", "/var/log", new RemoteSearchProgress { FoldersVisited = 12, CurrentFolder = "/var/log/nginx" }, "3 found"));
	}

	[Fact]
	public void Found_CountsWhatWasFoundAndWhere()
	{
		Assert.Equal("Nothing found in 1 folder", WalkStatus.Found(0, 1));
		Assert.Equal("48 found in 345 folders", WalkStatus.Found(48, 345));
	}

	[Theory]
	[InlineData(RemoteSearchLimit.Results, "Stopped at the 50 result limit.")]
	[InlineData(RemoteSearchLimit.Folders, "Stopped at the 200 folder limit.")]
	[InlineData(RemoteSearchLimit.Depth, "Folders more than 8 levels down were left out.")]
	[InlineData(RemoteSearchLimit.None, "")]
	public void Notes_NameTheCapThatStoppedTheWalk(RemoteSearchLimit limit, string expected)
	{
		RemoteSearchProgress progress = new() { Limit = limit };

		Assert.Equal(expected, WalkStatus.Notes(progress, Query, "result"));
		Assert.Equal(limit == RemoteSearchLimit.None, WalkStatus.IsComplete(progress));
	}

	[Fact]
	public void Notes_CountUnreadableFolders()
	{
		RemoteSearchProgress progress = new() { Limit = RemoteSearchLimit.Folders, UnreadableFolders = 12 };

		Assert.Equal("Stopped at the 200 folder limit. 12 folders could not be read.", WalkStatus.Notes(progress, Query, "result"));
		Assert.Equal("1 folder could not be read.", WalkStatus.Notes(new RemoteSearchProgress { UnreadableFolders = 1 }, Query, "file"));
		Assert.False(WalkStatus.IsComplete(progress));
	}
}
