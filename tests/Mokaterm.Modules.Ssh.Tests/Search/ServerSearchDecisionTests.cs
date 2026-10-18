using System.Text;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Modules.Ssh.Shell;

namespace Mokaterm.Modules.Ssh.Tests.Search;

/// <summary>When the search runs on the server and when the folder by folder walk answers instead, with canned runs.</summary>
public sealed class ServerSearchDecisionTests
{
	private const string Root = "/srv";

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task WithoutARun_TheWalkAnswers()
	{
		ListedTree tree = new();

		List<string> found = await SearchAsync(tree, run: null);

		Assert.Equal(["/srv/app.log"], found);
		Assert.Equal(["/srv"], tree.Listed);
	}

	[Fact]
	public async Task ARunThatSaysTheServerCannot_LeavesItToTheWalk()
	{
		ListedTree tree = new();
		int unusable = 0;

		List<string> found = await SearchAsync(tree, (_, _) => Task.FromResult<ReadOnlyMemory<byte>?>(null), () => unusable++);

		Assert.Equal(["/srv/app.log"], found);
		Assert.Equal(0, unusable);
	}

	[Fact]
	public async Task OutputThatIsNotTheScripts_LeavesItToTheWalk_AndSaysSo()
	{
		ListedTree tree = new();
		int unusable = 0;

		List<string> found = await SearchAsync(tree, Returning("whatever a login script printed"), () => unusable++);

		Assert.Equal(["/srv/app.log"], found);
		Assert.Equal(1, unusable);
	}

	[Fact]
	public async Task OutputPastTheByteBudget_LeavesTheQueryToTheWalk()
	{
		ListedTree tree = new();
		StringBuilder output = new("G\0");
		string miss = Match("./miss.txt");
		while (output.Length <= ServerSearchBudget.Default.MaxRecordBytes)
		{
			output.Append(miss);
		}

		List<string> found = await SearchAsync(tree, Returning(output + "\0U0\0E141\0"), query: new RemoteSearchQuery { Pattern = "?pp.log" });

		Assert.Equal(["/srv/app.log"], found);
		Assert.Equal(["/srv"], tree.Listed);
	}

	[Fact]
	public async Task TheServersOutput_IsWhatComesBack_WithAReportForEveryFolder_AndNothingListed()
	{
		ListedTree tree = new();
		RecordingProgress<RemoteSearchProgress> progress = new();

		List<string> found = await SearchAsync(
			tree,
			Returning("G\0D./logs\0" + Match("./logs/server.log") + "\0U1\0E1\0"),
			progress: progress);

		Assert.Equal(["/srv/logs/server.log"], found);
		Assert.Empty(tree.Listed);
		Assert.Equal(
			[
				new RemoteSearchProgress { FoldersVisited = 1, CurrentFolder = Root },
				new RemoteSearchProgress { FoldersVisited = 2, CurrentFolder = "/srv/logs" },
				new RemoteSearchProgress { FoldersVisited = 2, UnreadableFolders = 1, Found = 1 },
			],
			progress.Reports);
	}

	[Fact]
	public async Task AServerThatRanOutOfTime_HandsOverWhatItFound_ThenSaysSo()
	{
		ListedTree tree = new();
		List<string> found = [];

		RemoteFileSystemException failure = await Assert.ThrowsAsync<RemoteFileSystemException>(async () =>
		{
			await foreach (RemoteFileEntry entry in ServerSearch.SearchAsync(
				tree, Root, Query(), null, Returning("G\0" + Match("./app.log") + "\0U0\0E124\0"), () => { }, wholeSeconds: false, Ct))
			{
				found.Add(entry.Path);
			}
		}).WaitAsync(SearchShell.Limit, Ct);

		Assert.Equal(["/srv/app.log"], found);
		Assert.Equal(ServerSearch.TimedOutMessage(ServerSearchBudget.Default), failure.Message);
	}

	[Theory]
	[InlineData(0, 1, 1)]
	[InlineData(1, 0, 1)]
	[InlineData(1, 1, 0)]
	public async Task Caps_MustBePositive_AsTheWalkSays(int maxResults, int maxFolders, int maxDepth)
	{
		RemoteSearchQuery query = Query() with { MaxResults = maxResults, MaxFolders = maxFolders, MaxDepth = maxDepth };

		await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => SearchAsync(new ListedTree(), Returning(""), query: query)).WaitAsync(SearchShell.Limit, Ct);
	}

	private static RemoteSearchQuery Query() => new() { Pattern = "*.log" };

	private static string Match(string path) => "Mf\0" + "3\0" + "1700000000\0" + "644\0" + "u\0" + "g\0" + "\0" + "\0" + path + "\0";

	private static Func<IReadOnlyList<string>, CancellationToken, Task<ReadOnlyMemory<byte>?>> Returning(string output) =>
		(_, _) => Task.FromResult<ReadOnlyMemory<byte>?>(Encoding.UTF8.GetBytes(output));

	private static async Task<List<string>> SearchAsync(
		ListedTree tree,
		Func<IReadOnlyList<string>, CancellationToken, Task<ReadOnlyMemory<byte>?>>? run,
		Action? unusable = null,
		RemoteSearchQuery? query = null,
		IProgress<RemoteSearchProgress>? progress = null)
	{
		using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(Ct);
		limit.CancelAfter(SearchShell.Limit);
		List<string> found = [];
		await foreach (RemoteFileEntry entry in ServerSearch.SearchAsync(tree, Root, query ?? Query(), progress, run, unusable ?? (() => { }), wholeSeconds: false, limit.Token))
		{
			found.Add(entry.Path);
		}

		return found;
	}

	/// <summary>One folder holding one log, listed only when the walk runs.</summary>
	private sealed class ListedTree : IRemoteFileSystem
	{
		public List<string> Listed { get; } = [];

		public RemoteFileSystemFeatures Features => RemoteFileSystemFeatures.None;

		public string UserName => "abc";

		public bool IsElevated => false;

		public ValueTask<IReadOnlyList<RemoteFileEntry>> ListAsync(string path, CancellationToken cancellationToken = default)
		{
			Listed.Add(path);
			return ValueTask.FromResult<IReadOnlyList<RemoteFileEntry>>(
				path == Root ? [new RemoteFileEntry { Name = "app.log", Path = "/srv/app.log", Kind = RemoteEntryKind.File, Size = 3 }] : []);
		}

		public ValueTask<string> GetHomeDirectoryAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(Root);

		public ValueTask<string> ResolvePathAsync(string path, CancellationToken cancellationToken = default) => ValueTask.FromResult(path);

		public ValueTask<RemoteFileEntry?> StatAsync(string path, CancellationToken cancellationToken = default) => ValueTask.FromResult<RemoteFileEntry?>(null);

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
	}
}
