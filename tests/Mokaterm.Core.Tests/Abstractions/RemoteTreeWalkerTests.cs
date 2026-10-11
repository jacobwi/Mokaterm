using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.Core.Tests.Abstractions;

/// <summary>The default <see cref="IRemoteFileSystem.SearchAsync"/>, as a file system that brings no search of its own gets it.</summary>
public sealed class RemoteTreeWalkerTests
{
	private const string Site = "/srv/site";

	[Fact]
	public async Task Glob_MatchesTheWholeName()
	{
		List<string> found = await SearchAsync(new TreeFileSystem(), Site, Query("*.log"));

		Assert.Equal(["/srv/site/error.log", "/srv/site/logs/access.log"], found);
	}

	[Fact]
	public async Task PlainText_MatchesPartOfTheNameInAnyCase_NearestFirst()
	{
		List<string> found = await SearchAsync(new TreeFileSystem(), Site, Query("access"));

		Assert.Equal(["/srv/site/css/Access.css", "/srv/site/logs/access.log", "/srv/site/logs/deep/access.log.2"], found);
	}

	[Fact]
	public async Task MatchCase_LeavesOtherCasesOut()
	{
		List<string> found = await SearchAsync(new TreeFileSystem(), Site, Query("access") with { MatchCase = true });

		Assert.Equal(["/srv/site/logs/access.log", "/srv/site/logs/deep/access.log.2"], found);
	}

	[Fact]
	public async Task HiddenEntries_AreNeitherReturnedNorEntered()
	{
		TreeFileSystem tree = new();

		List<string> found = await SearchAsync(tree, Site, Query("*"));

		Assert.DoesNotContain(found, path => path.Contains("/.", StringComparison.Ordinal));
		Assert.DoesNotContain("list /srv/site/.git", tree.Calls);
	}

	[Fact]
	public async Task IncludeHidden_ReturnsAndEntersThem()
	{
		TreeFileSystem tree = new();

		List<string> found = await SearchAsync(tree, Site, Query("*") with { IncludeHidden = true });

		Assert.Contains("/srv/site/.env", found);
		Assert.Contains("/srv/site/.git/config", found);
		Assert.Contains("list /srv/site/.git", tree.Calls);
	}

	[Fact]
	public async Task FoldersOnly_IncludesLinksToFolders()
	{
		List<string> found = await SearchAsync(new TreeFileSystem(), Site, Query("*") with { Kinds = RemoteSearchKinds.Folders });

		Assert.Equal(["/srv/site/css", "/srv/site/logs", "/srv/site/loop", "/srv/site/secret", "/srv/site/logs/deep"], found);
	}

	[Fact]
	public async Task FilesOnly_IncludesLinksToFiles()
	{
		List<string> found = await SearchAsync(new TreeFileSystem(), Site, Query("*") with { Kinds = RemoteSearchKinds.Files });

		Assert.Contains("/srv/site/current", found);
		Assert.DoesNotContain("/srv/site/css", found);
		Assert.DoesNotContain("/srv/site/loop", found);
	}

	[Fact]
	public async Task Walk_IsBreadthFirst()
	{
		TreeFileSystem tree = new();

		await SearchAsync(tree, Site, Query("*"));

		Assert.Equal(["list /srv/site", "list /srv/site/css", "list /srv/site/logs", "list /srv/site/secret", "list /srv/site/logs/deep"], tree.Calls);
	}

	// The link points back at the folder that holds it; following it would never end.
	[Fact]
	public async Task LinksToFolders_AreReturnedButNotFollowed()
	{
		TreeFileSystem tree = new();

		List<string> found = await SearchAsync(tree, Site, Query("loop"));

		Assert.Equal(["/srv/site/loop"], found);
		Assert.DoesNotContain(tree.Calls, call => call.StartsWith("list /srv/site/loop", StringComparison.Ordinal));
	}

	[Fact]
	public async Task UnreadableFolders_AreCountedAndTheWalkGoesOn()
	{
		TreeFileSystem tree = new();
		tree.Denied.Add("/srv/site/secret");
		tree.Missing.Add("/srv/site/css");
		RecordingProgress<RemoteSearchProgress> progress = new();

		List<string> found = await SearchAsync(tree, Site, Query("access"), progress);

		Assert.Equal(["/srv/site/logs/access.log", "/srv/site/logs/deep/access.log.2"], found);
		RemoteSearchProgress last = progress.Reports.Last();
		Assert.Equal(2, last.UnreadableFolders);
		Assert.Equal(RemoteSearchLimit.None, last.Limit);
	}

	[Fact]
	public async Task UnreadableRoot_Throws()
	{
		TreeFileSystem tree = new();
		tree.Denied.Add(Site);

		RemoteFileSystemException failure = await Assert.ThrowsAsync<RemoteFileSystemException>(() => SearchAsync(tree, Site, Query("*")));

		Assert.Equal(RemoteFileErrorKind.PermissionDenied, failure.Kind);
	}

	[Fact]
	public async Task ResultCap_StopsTheWalkAndSaysSo()
	{
		TreeFileSystem tree = new();
		RecordingProgress<RemoteSearchProgress> progress = new();

		List<string> found = await SearchAsync(tree, Site, Query("*") with { MaxResults = 2 }, progress);

		Assert.Equal(["/srv/site/css", "/srv/site/current"], found);
		Assert.Equal(["list /srv/site"], tree.Calls);
		Assert.Equal(RemoteSearchLimit.Results, progress.Reports.Last().Limit);
	}

	[Fact]
	public async Task ResultCap_ReachedWithNothingLeft_IsNoLimit()
	{
		RecordingProgress<RemoteSearchProgress> progress = new();

		List<string> found = await SearchAsync(new TreeFileSystem(), "/srv/site/logs/deep", Query("*") with { MaxResults = 1 }, progress);

		Assert.Equal(["/srv/site/logs/deep/access.log.2"], found);
		Assert.Equal(RemoteSearchLimit.None, progress.Reports.Last().Limit);
	}

	[Fact]
	public async Task FolderCap_StopsTheWalkAndSaysSo()
	{
		TreeFileSystem tree = new();
		RecordingProgress<RemoteSearchProgress> progress = new();

		List<string> found = await SearchAsync(tree, Site, Query("access") with { MaxFolders = 2 }, progress);

		Assert.Equal(["/srv/site/css/Access.css"], found);
		Assert.Equal(["list /srv/site", "list /srv/site/css"], tree.Calls);
		RemoteSearchProgress last = progress.Reports.Last();
		Assert.Equal(RemoteSearchLimit.Folders, last.Limit);
		Assert.Equal(2, last.FoldersVisited);
	}

	[Fact]
	public async Task DepthCap_LeavesDeeperFoldersOutAndSaysSo()
	{
		TreeFileSystem tree = new();
		RecordingProgress<RemoteSearchProgress> progress = new();

		List<string> found = await SearchAsync(tree, Site, Query("access") with { MaxDepth = 2 }, progress);

		Assert.Equal(["/srv/site/css/Access.css", "/srv/site/logs/access.log"], found);
		Assert.DoesNotContain("list /srv/site/logs/deep", tree.Calls);
		Assert.Equal(RemoteSearchLimit.Depth, progress.Reports.Last().Limit);
	}

	[Fact]
	public async Task DepthOfOne_ReadsOnlyTheRoot()
	{
		TreeFileSystem tree = new();

		List<string> found = await SearchAsync(tree, Site, Query("*.log") with { MaxDepth = 1 });

		Assert.Equal(["/srv/site/error.log"], found);
		Assert.Equal(["list /srv/site"], tree.Calls);
	}

	[Fact]
	public async Task Cancellation_IsCheckedBeforeEveryListing()
	{
		TreeFileSystem tree = new() { IgnoresCancellation = true };
		using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

		await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
		{
			await foreach (RemoteFileEntry entry in ((IRemoteFileSystem)tree).SearchAsync(Site, Query("index"), cancellationToken: stop.Token))
			{
				await stop.CancelAsync();
			}
		});

		Assert.Equal(["list /srv/site"], tree.Calls);
	}

	[Fact]
	public async Task Progress_NamesEachFolderAndEndsWithTheTotals()
	{
		RecordingProgress<RemoteSearchProgress> progress = new();

		await SearchAsync(new TreeFileSystem(), "/srv/site/logs", Query("access"), progress);

		List<string?> folders = [.. progress.Reports.Select(report => report.CurrentFolder)];
		Assert.Equal(["/srv/site/logs", "/srv/site/logs/deep", null], folders);
		Assert.Equal(new RemoteSearchProgress { FoldersVisited = 2, Found = 2 }, progress.Reports.Last());
	}

	[Theory]
	[InlineData(0, 1, 1)]
	[InlineData(1, 0, 1)]
	[InlineData(1, 1, 0)]
	public async Task Caps_MustBePositive(int maxResults, int maxFolders, int maxDepth)
	{
		RemoteSearchQuery query = Query("*") with { MaxResults = maxResults, MaxFolders = maxFolders, MaxDepth = maxDepth };

		await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => SearchAsync(new TreeFileSystem(), Site, query));
	}

	[Theory]
	[InlineData("*.log", "error.log", true)]
	[InlineData("*.log", "error.log.1", false)]
	[InlineData("*.LOG", "error.log", true)]
	[InlineData("error.???", "error.log", true)]
	[InlineData("error.?", "error.log", false)]
	[InlineData("a*b*c", "aXXbYYc", true)]
	[InlineData("a*b*c", "aXXbYY", false)]
	[InlineData("*a*", "banana", true)]
	[InlineData("**", "x", true)]
	[InlineData("a\\*", "a\\b", true)]
	[InlineData("log", "Error.LOG.1", true)]
	[InlineData("[ab]", "x[ab]y", true)]
	[InlineData("", "anything", true)]
	public void Name_GlobOrPieceIgnoringCase(string pattern, string name, bool expected) =>
		Assert.Equal(expected, Query(pattern).MatchesName(name));

	[Theory]
	[InlineData("*.LOG", "error.log", false)]
	[InlineData("LOG", "error.log", false)]
	[InlineData("log", "error.log", true)]
	public void Name_WithMatchCase(string pattern, string name, bool expected) =>
		Assert.Equal(expected, (Query(pattern) with { MatchCase = true }).MatchesName(name));

	private static RemoteSearchQuery Query(string pattern) => new() { Pattern = pattern };

	private static async Task<List<string>> SearchAsync(
		IRemoteFileSystem fileSystem,
		string root,
		RemoteSearchQuery query,
		IProgress<RemoteSearchProgress>? progress = null)
	{
		List<string> paths = [];
		await foreach (RemoteFileEntry entry in fileSystem.SearchAsync(root, query, progress, TestContext.Current.CancellationToken))
		{
			paths.Add(entry.Path);
		}

		return paths;
	}

	/// <summary>A small fixed tree that implements only the members without a default, and records every listing.</summary>
	private sealed class TreeFileSystem : IRemoteFileSystem
	{
		private static readonly RemoteFileEntry[] Entries =
		[
			Folder("/srv/site"),
			File("/srv/site/index.html"),
			File("/srv/site/error.log"),
			File("/srv/site/error.log.1"),
			File("/srv/site/.env"),
			Folder("/srv/site/.git"),
			File("/srv/site/.git/config"),
			Folder("/srv/site/css"),
			File("/srv/site/css/Access.css"),
			File("/srv/site/css/app.css"),
			Folder("/srv/site/logs"),
			File("/srv/site/logs/access.log"),
			Folder("/srv/site/logs/deep"),
			File("/srv/site/logs/deep/access.log.2"),
			Folder("/srv/site/secret"),
			File("/srv/site/secret/token.txt"),
			Link("/srv/site/loop", RemoteEntryKind.Directory),
			Link("/srv/site/current", RemoteEntryKind.File),
		];

		public List<string> Calls { get; } = [];

		public HashSet<string> Denied { get; } = new(StringComparer.Ordinal);

		public HashSet<string> Missing { get; } = new(StringComparer.Ordinal);

		/// <summary>Lists even after cancellation, so only the walk's own check can stop it.</summary>
		public bool IgnoresCancellation { get; init; }

		public RemoteFileSystemFeatures Features => RemoteFileSystemFeatures.SymbolicLinks;

		public string UserName => "abc";

		public bool IsElevated => false;

		public ValueTask<string> GetHomeDirectoryAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult("/home/abc");

		public ValueTask<string> ResolvePathAsync(string path, CancellationToken cancellationToken = default) => ValueTask.FromResult(path);

		public ValueTask<IReadOnlyList<RemoteFileEntry>> ListAsync(string path, CancellationToken cancellationToken = default)
		{
			Calls.Add("list " + path);
			if (!IgnoresCancellation)
			{
				cancellationToken.ThrowIfCancellationRequested();
			}

			if (Denied.Contains(path))
			{
				throw new RemoteFileSystemException(RemoteFileErrorKind.PermissionDenied, "Permission denied", path);
			}

			if (Missing.Contains(path) || !Entries.Any(entry => entry.Path == path && entry.Kind == RemoteEntryKind.Directory))
			{
				throw new RemoteFileSystemException(RemoteFileErrorKind.NotFound, "No such folder", path);
			}

			// Listed out of order on purpose: a server's listing has none, and the walk puts its own on it.
			return ValueTask.FromResult<IReadOnlyList<RemoteFileEntry>>([.. Entries.Where(entry => RemotePath.GetParent(entry.Path) == path && entry.Path != path).Reverse()]);
		}

		public ValueTask<RemoteFileEntry?> StatAsync(string path, CancellationToken cancellationToken = default) =>
			ValueTask.FromResult(Entries.FirstOrDefault(entry => entry.Path == path));

		public ValueTask CreateDirectoryAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public ValueTask DeleteAsync(string path, bool recursive, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public ValueTask RenameAsync(string sourcePath, string destinationPath, bool overwrite, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();

		public ValueTask SetPermissionsAsync(string path, UnixFileMode permissions, bool recursive, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();

		public ValueTask SetOwnerAsync(string path, string owner, string? group, bool recursive, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();

		public ValueTask<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public ValueTask UploadAsync(string path, Stream source, UploadOptions options, IProgress<long>? progress = null, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();

		public ValueTask DownloadAsync(string path, Stream destination, IProgress<long>? progress = null, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();

		public ValueTask DisposeAsync() => ValueTask.CompletedTask;

		private static RemoteFileEntry File(string path) => new() { Name = RemotePath.GetName(path), Path = path, Kind = RemoteEntryKind.File, Size = 10 };

		private static RemoteFileEntry Folder(string path) => new() { Name = RemotePath.GetName(path), Path = path, Kind = RemoteEntryKind.Directory };

		private static RemoteFileEntry Link(string path, RemoteEntryKind targetKind) => new()
		{
			Name = RemotePath.GetName(path),
			Path = path,
			Kind = RemoteEntryKind.SymbolicLink,
			LinkTarget = targetKind == RemoteEntryKind.Directory ? "." : "index.html",
			LinkTargetKind = targetKind,
		};
	}
}
