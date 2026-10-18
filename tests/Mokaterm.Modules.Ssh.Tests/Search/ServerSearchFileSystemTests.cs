using Microsoft.Extensions.Logging.Abstractions;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Modules.Ssh.Docker;
using Mokaterm.Modules.Ssh.Elevation;
using Mokaterm.Modules.Ssh.FileSystem;
using Mokaterm.Modules.Ssh.Shell;
using Mokaterm.Modules.Ssh.Tests.Fakes;

namespace Mokaterm.Modules.Ssh.Tests.Search;

/// <summary>
/// The search of the root view (through a stand-in sudo) and of a container's files (through a stand-in docker exec),
/// both running the real scripts in a local POSIX shell, against the folder by folder walk over the same file system:
/// the same entries and the same ending for every query, in one script instead of a listing per folder, and the walk
/// itself where the server cannot search. Skipped where there is no sh.
/// </summary>
public sealed class ServerSearchFileSystemTests : IAsyncLifetime
{
	private const string Container = "web-1";

	private const string WithoutAnyForm = """
		for a do
			case $a in -printf|-fprintf|-xtype|-print0) printf 'find: unrecognized: %s\n' "$a" >&2; exit 1 ;; esac
		done
		exec /usr/bin/find "$@"
		""";

	private const string SilentForever = """
		case " $* " in *" -maxdepth 0 "*) exit 0 ;; esac
		exec sleep 60
		""";

	private readonly string? _found = PosixShellLocator.Find();
	private SearchShell? _shell;
	private string _site = "";

	public static TheoryData<string, bool, bool, RemoteSearchKinds, int> Queries => new()
	{
		{ "*.log", false, false, RemoteSearchKinds.All, RemoteSearchQuery.DefaultMaxDepth },
		{ "access", true, false, RemoteSearchKinds.All, RemoteSearchQuery.DefaultMaxDepth },
		{ "error.?", false, false, RemoteSearchKinds.All, RemoteSearchQuery.DefaultMaxDepth },
		{ "CAFÉ*", false, false, RemoteSearchKinds.All, RemoteSearchQuery.DefaultMaxDepth },
		{ "SETUP.*", false, false, RemoteSearchKinds.All, RemoteSearchQuery.DefaultMaxDepth },
		{ "*", false, true, RemoteSearchKinds.Folders, RemoteSearchQuery.DefaultMaxDepth },
		{ "*", false, false, RemoteSearchKinds.Files, 2 },
		{ "e", false, true, RemoteSearchKinds.All, 3 },
	};

	private SearchShell Shell
	{
		get
		{
			if (_shell is null)
			{
				Assert.Skip("No POSIX sh is available on this machine.");
			}

			return _shell;
		}
	}

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	public async ValueTask InitializeAsync()
	{
		if (_found is not null)
		{
			_shell = new SearchShell(_found);
			_site = await _shell.MakeSiteAsync();
		}
	}

	public async ValueTask DisposeAsync()
	{
		if (_shell is not null)
		{
			await _shell.DisposeAsync();
		}
	}

	[Theory]
	[MemberData(nameof(Queries))]
	public async Task RootView_SearchesWithOneScript_AndFindsWhatTheWalkFinds(string pattern, bool matchCase, bool includeHidden, RemoteSearchKinds kinds, int depth)
	{
		RemoteSearchQuery query = new() { Pattern = pattern, MatchCase = matchCase, IncludeHidden = includeHidden, Kinds = kinds, MaxDepth = depth };
		using LocalSudoShell sudo = new(Shell.Shell);
		await using SudoFileSystem files = Open(sudo);

		(List<RemoteFileEntry> walked, RemoteSearchProgress walkEnd) = await CollectAsync(progress => RemoteTreeWalker.SearchAsync(files, _site, query, progress, Ct));
		int before = sudo.Calls.Count;
		(List<RemoteFileEntry> searched, RemoteSearchProgress searchEnd) = await CollectAsync(progress => files.SearchAsync(_site, query, progress, Ct));

		Assert.Equal(ByPath(walked), ByPath(searched));
		Assert.Equal(walkEnd, searchEnd);
		LocalSudoShell.Call call = Assert.Single(sudo.Calls.Skip(before));
		Assert.Contains(PosixShell.Quote(RemoteScripts.ElevatedPreamble + RemoteScripts.Search), call.Command, StringComparison.Ordinal);
		Assert.True(call.RunsLong);
	}

	[Fact]
	public async Task Container_SearchesWithOneExec_AndFindsWhatTheWalkFinds()
	{
		ScriptedContainer docker = new(Shell.Shell, Container);
		ContainerFileSystem files = await ContainerFileSystem.OpenAsync(() => new DockerClient(docker, "build-01"), Container, null, null, Ct).WaitAsync(SearchShell.Limit, Ct);
		RemoteSearchQuery query = new() { Pattern = "*.log" };

		(List<RemoteFileEntry> walked, RemoteSearchProgress walkEnd) = await CollectAsync(progress => RemoteTreeWalker.SearchAsync(files, _site, query, progress, Ct));
		int before = docker.Calls.Count;
		(List<RemoteFileEntry> searched, RemoteSearchProgress searchEnd) = await CollectAsync(progress => files.SearchAsync(_site, query, progress, Ct));

		Assert.Equal(ByPath(walked), ByPath(searched));
		Assert.Equal(walkEnd, searchEnd);
		DockerCall call = Assert.Single(docker.Calls.Skip(before));
		Assert.Contains(RemoteScripts.Search, call.Arguments);
		Assert.True(DockerShellRun.Request(call, DockerCommandLine.Build(call)).RunsLong);
	}

	[Fact]
	public async Task OnAServerThatCannotSearch_TheWalkAnswers_AndTheServerIsNotAskedAgain()
	{
		string find = await Shell.FakeFindAsync(WithoutAnyForm);
		using LocalSudoShell sudo = new(Shell.Shell, find);
		await using SudoFileSystem files = Open(sudo);
		RemoteSearchQuery query = new() { Pattern = "*.log" };

		(List<RemoteFileEntry> walked, _) = await CollectAsync(progress => RemoteTreeWalker.SearchAsync(files, _site, query, progress, Ct));
		(List<RemoteFileEntry> first, _) = await CollectAsync(progress => files.SearchAsync(_site, query, progress, Ct));
		int before = sudo.Calls.Count;
		(List<RemoteFileEntry> second, _) = await CollectAsync(progress => files.SearchAsync(_site, query, progress, Ct));

		Assert.Equal(ByPath(walked), ByPath(first));
		Assert.Equal(ByPath(walked), ByPath(second));
		Assert.Single(sudo.Calls, call => call.Command.Contains(PosixShell.Quote(RemoteScripts.ElevatedPreamble + RemoteScripts.Search), StringComparison.Ordinal));
		Assert.All(sudo.Calls.Skip(before), call => Assert.Contains(PosixShell.Quote(RemoteScripts.ElevatedPreamble + RemoteScripts.List), call.Command, StringComparison.Ordinal));
	}

	[Fact]
	public async Task CancellingASearch_StopsItWhileTheServerStillRunsIt()
	{
		string find = await Shell.FakeFindAsync(SilentForever);
		using LocalSudoShell sudo = new(Shell.Shell, find);
		await using SudoFileSystem files = Open(sudo);
		using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
		stop.CancelAfter(TimeSpan.FromSeconds(1));

		Task searching = CollectAsync(progress => files.SearchAsync(_site, new RemoteSearchQuery { Pattern = "*" }, progress, stop.Token));

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => searching.WaitAsync(TimeSpan.FromSeconds(20), Ct));
	}

	private static SudoFileSystem Open(LocalSudoShell sudo) =>
		new(
			sudo.Runner(),
			_ => ValueTask.FromException<SftpFileSystem>(new InvalidOperationException("Searches never need the login's SFTP.")),
			new RemoteAccountNames(() => null, NullLogger.Instance),
			"/tmp",
			NullLogger.Instance,
			_ => { });

	private static async Task<(List<RemoteFileEntry> Entries, RemoteSearchProgress Last)> CollectAsync(
		Func<IProgress<RemoteSearchProgress>, IAsyncEnumerable<RemoteFileEntry>> search)
	{
		// The search's own token is the caller's; this one only keeps a hung search from holding the run.
		using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(Ct);
		limit.CancelAfter(SearchShell.Limit);
		RecordingProgress<RemoteSearchProgress> progress = new();
		List<RemoteFileEntry> entries = [];
		await foreach (RemoteFileEntry entry in search(progress).WithCancellation(limit.Token))
		{
			entries.Add(entry);
		}

		return (entries, progress.Reports.Last());
	}

	private static List<RemoteFileEntry> ByPath(List<RemoteFileEntry> entries) => [.. entries.OrderBy(entry => entry.Path, StringComparer.Ordinal)];
}
