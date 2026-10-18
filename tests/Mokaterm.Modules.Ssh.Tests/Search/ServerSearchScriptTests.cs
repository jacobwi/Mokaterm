using System.Text;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Modules.Ssh.Shell;

namespace Mokaterm.Modules.Ssh.Tests.Search;

/// <summary>
/// The search script run for real against GNU find in a local POSIX shell (Git Bash on Windows), and against stand-in
/// finds put first on the PATH: one without <c>-printf</c> for the shell fallback, ones that never stop for the counter,
/// one that sleeps for the time limit. Skipped where there is no sh.
/// </summary>
public sealed class ServerSearchScriptTests : IAsyncLifetime
{
	// Answers the script's probe of what it can do, then prints matches until something stops it.
	private const string EndlessMatches = """
		case " $* " in *" -maxdepth 0 "*) exit 0 ;; esac
		while :; do printf 'M%s\0%s\0%s\0%s\0%s\0%s\0%s\0%s\0%s\0' f 1 1700000000 644 u g '' f ./hit.log; done
		""";

	private const string EndlessMisses = """
		case " $* " in *" -maxdepth 0 "*) exit 0 ;; esac
		while :; do printf 'M%s\0%s\0%s\0%s\0%s\0%s\0%s\0%s\0%s\0' f 1 1700000000 644 u g '' f ./miss.txt; done
		""";

	private const string EndlessFolders = """
		case " $* " in *" -maxdepth 0 "*) exit 0 ;; esac
		while :; do printf 'D%s\0' ./a ./b ./c ./d; done
		""";

	private const string SilentAfterOneFolder = """
		case " $* " in *" -maxdepth 0 "*) exit 0 ;; esac
		printf 'D%s\0' ./a
		exec sleep 30
		""";

	// BusyBox's find has no -printf and no -xtype; the BSDs' has neither, nor print0 in old releases.
	private const string WithoutPrintf = """
		for a do
			case $a in -printf|-fprintf|-xtype) printf 'find: unrecognized: %s\n' "$a" >&2; exit 1 ;; esac
		done
		exec /usr/bin/find "$@"
		""";

	private const string WithoutAnyForm = """
		for a do
			case $a in -printf|-fprintf|-xtype|-print0) printf 'find: unrecognized: %s\n' "$a" >&2; exit 1 ;; esac
		done
		exec /usr/bin/find "$@"
		""";

	private readonly string? _found = PosixShellLocator.Find();
	private SearchShell? _shell;
	private string _site = "";

	public static TheoryData<string, bool, bool, RemoteSearchKinds> Queries => new()
	{
		{ "*.log", false, false, RemoteSearchKinds.All },
		{ "*.LOG", false, false, RemoteSearchKinds.All },
		{ "*.LOG", true, false, RemoteSearchKinds.All },
		{ "access", false, false, RemoteSearchKinds.All },
		{ "access", true, false, RemoteSearchKinds.All },
		{ "?rror.log", false, false, RemoteSearchKinds.All },
		{ "error.???", false, false, RemoteSearchKinds.Files },
		{ "-rf*", false, false, RemoteSearchKinds.All },
		{ "it's \"quoted\"", false, false, RemoteSearchKinds.All },
		{ "new\nline", false, false, RemoteSearchKinds.All },
		{ "café", false, false, RemoteSearchKinds.All },
		{ "CAFÉ.TXT", false, false, RemoteSearchKinds.All },
		{ "CAFÉ.TXT", true, false, RemoteSearchKinds.All },
		{ "SETUP.*", false, false, RemoteSearchKinds.All },
		{ "setup", false, false, RemoteSearchKinds.All },
		{ "[ab]", false, false, RemoteSearchKinds.All },
		{ "a\\b", false, false, RemoteSearchKinds.All },
		{ "*", false, false, RemoteSearchKinds.Folders },
		{ "*", false, false, RemoteSearchKinds.Files },
		{ "*", false, true, RemoteSearchKinds.All },
		{ "git", false, true, RemoteSearchKinds.Folders },
		{ "e", false, true, RemoteSearchKinds.Files },
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

	[Fact]
	public async Task Search_NamesWithSpacesNewlinesQuotesAndADash_ComeBackWhole()
	{
		ServerSearchResult result = await Shell.SearchAsync(_site, Query("*.log"));

		Assert.Equal(
			["-dash/-rf.log", "a/b/c/d/deep.log", "error.log", "logs/access.log", "new\nline.log", "sub dir/it's \"quoted\".log"],
			Relative(result));
		RemoteFileEntry odd = Assert.Single(Entries(result), entry => entry.Name == "new\nline.log");
		Assert.Equal(RemotePath.Combine(_site, "new\nline.log"), odd.Path);
		Assert.Equal(RemoteEntryKind.File, odd.Kind);
		Assert.Equal(1, odd.Size);
		Assert.NotNull(odd.LastModified);
		Assert.NotNull(odd.Permissions);
		Assert.Equal(RemoteSearchLimit.None, result.Limit);
	}

	[Fact]
	public async Task Search_APatternThatLooksLikeAnOption_IsAName()
	{
		ServerSearchResult result = await Shell.SearchAsync(_site, Query("-rf*") with { MatchCase = true });

		Assert.Equal(["-dash/-rf.log"], Relative(result));
	}

	[Fact]
	public async Task Search_LinksToFolders_AreFoundButNeverEntered()
	{
		ServerSearchResult result = await Shell.SearchAsync(_site, Query("*"));
		List<RemoteFileEntry> entries = Entries(result);

		RemoteFileEntry loop = Assert.Single(entries, entry => entry.Name == "loop");
		Assert.Equal(RemoteEntryKind.SymbolicLink, loop.Kind);
		Assert.Equal(RemoteEntryKind.Directory, loop.LinkTargetKind);
		Assert.Equal(".", loop.LinkTarget);
		Assert.Equal(RemoteEntryKind.File, Assert.Single(entries, entry => entry.Name == "current").LinkTargetKind);
		Assert.Null(Assert.Single(entries, entry => entry.Name == "broken").LinkTargetKind);
		Assert.DoesNotContain(Relative(result), path => path.StartsWith("loop/", StringComparison.Ordinal));
		Assert.DoesNotContain(result.Steps, step => step.Folder?.EndsWith("/loop", StringComparison.Ordinal) == true);

		ServerSearchResult folders = await Shell.SearchAsync(_site, Query("*") with { Kinds = RemoteSearchKinds.Folders });
		Assert.Contains("loop", Relative(folders));
		Assert.DoesNotContain("current", Relative(folders));
	}

	[Fact]
	public async Task Search_AFolderItCannotRead_IsCountedAndSkipped()
	{
		if (!await Shell.LockAsync(_site + "/secret"))
		{
			Assert.Skip("This machine cannot make a folder unreadable (running as root, or no ACLs).");
		}

		ServerSearchResult result = await Shell.SearchAsync(_site, Query("*"));

		Assert.Equal(1, result.UnreadableFolders);
		Assert.Contains("secret", Relative(result));
		Assert.DoesNotContain("secret/token.txt", Relative(result));
		Assert.Equal(RemoteSearchLimit.None, result.Limit);
	}

	[Fact]
	public async Task Search_KeepsToTheDepthCap_AndSaysSo()
	{
		ServerSearchResult top = await Shell.SearchAsync(_site, Query("*") with { MaxDepth = 1 });
		ServerSearchResult four = await Shell.SearchAsync(_site, Query("deep.log") with { MaxDepth = 4 });
		ServerSearchResult five = await Shell.SearchAsync(_site, Query("deep.log") with { MaxDepth = 5 });

		Assert.All(Relative(top), path => Assert.DoesNotContain('/', path));
		Assert.Contains("logs", Relative(top));
		Assert.Equal(1, top.FoldersVisited);
		Assert.Equal(RemoteSearchLimit.Depth, top.Limit);
		Assert.Empty(Relative(four));
		Assert.Equal(RemoteSearchLimit.Depth, four.Limit);
		Assert.Equal(["a/b/c/d/deep.log"], Relative(five));
		Assert.Equal(RemoteSearchLimit.None, five.Limit);
	}

	[Fact]
	public async Task Search_ResultCap_StopsAndSaysSo()
	{
		ServerSearchResult result = await Shell.SearchAsync(_site, Query("*") with { MaxResults = 3 });

		Assert.Equal(3, Entries(result).Count);
		Assert.Equal(RemoteSearchLimit.Results, result.Limit);
	}

	[Fact]
	public async Task Search_FolderCap_StopsAndSaysSo()
	{
		ServerSearchResult result = await Shell.SearchAsync(_site, Query("*") with { MaxFolders = 2 });

		Assert.Equal(2, result.FoldersVisited);
		Assert.Equal(RemoteSearchLimit.Folders, result.Limit);
	}

	[Fact]
	public async Task Search_AFindThatNeverEnds_IsStoppedPastTheResultCap()
	{
		string find = await Shell.FakeFindAsync(EndlessMatches);

		ServerSearchResult result = await Shell.SearchAsync(_site, Query("*.log") with { MaxResults = 5 }, firstOnPath: find);

		Assert.Equal(5, Entries(result).Count);
		Assert.Equal(RemoteSearchLimit.Results, result.Limit);
	}

	[Fact]
	public async Task Search_AFindThatNeverEnds_IsStoppedPastTheFolderCap()
	{
		string find = await Shell.FakeFindAsync(EndlessFolders);

		ServerSearchResult result = await Shell.SearchAsync(_site, Query("*.log") with { MaxFolders = 50 }, firstOnPath: find);

		Assert.Equal(RemoteSearchLimit.Folders, result.Limit);
		Assert.InRange(result.FoldersVisited, 2, 50);
	}

	// "?" is a pattern find cannot say exactly, so the script never stops on matches and the byte budget has to.
	[Fact]
	public async Task Search_PastTheByteBudget_LeavesTheQueryToTheWalk()
	{
		string find = await Shell.FakeFindAsync(EndlessMisses);

		ServerSearchResult result = await Shell.SearchAsync(
			_site,
			Query("?.log"),
			new ServerSearchBudget(TimeSpan.FromMinutes(1), 64 * 1024),
			find);

		Assert.True(result.NeedsWalk);
		Assert.Empty(Entries(result));
	}

	[Fact]
	public async Task Search_ThatRunsOutOfTime_IsStoppedByTheServer_AndSaysSo()
	{
		string find = await Shell.FakeFindAsync(SilentAfterOneFolder);

		ServerSearchResult result = await Shell.SearchAsync(_site, Query("*"), new ServerSearchBudget(TimeSpan.FromSeconds(2), 1024 * 1024), find);

		Assert.True(result.TimedOut);
		Assert.Equal(2, result.FoldersVisited);
	}

	[Fact]
	public async Task Search_WithoutPrintf_ReadsTheSameEntriesThroughTheShell()
	{
		string find = await Shell.FakeFindAsync(WithoutPrintf);
		RemoteSearchQuery[] queries =
		[
			Query("*.log"),
			Query("access"),
			Query("CAFÉ.TXT"),
			Query("*") with { Kinds = RemoteSearchKinds.Folders },
			Query("*") with { Kinds = RemoteSearchKinds.Files, IncludeHidden = true },
		];

		RemoteCommandResult raw = await Shell.RunAsync(_site, queries[0], firstOnPath: find);
		Assert.True(RemoteOutput.TryGetPayload(raw.Output, out ReadOnlyMemory<byte> payload));
		Assert.Equal("P\0", Encoding.UTF8.GetString(payload.Span[..2]));
		foreach (RemoteSearchQuery query in queries)
		{
			ServerSearchResult printed = await Shell.SearchAsync(_site, query);
			ServerSearchResult stated = await Shell.SearchAsync(_site, query, firstOnPath: find);

			// stat -c reads whole seconds where -printf reads fractions; everything else is the same entry.
			Assert.Equal(WholeSeconds(printed), WholeSeconds(stated));
			Assert.Equal(printed.FoldersVisited, stated.FoldersVisited);
			Assert.Equal(printed.Limit, stated.Limit);
		}
	}

	[Fact]
	public async Task Search_OnAServerWithNoFindThatCanPrintIt_SaysItIsUnsupported()
	{
		string find = await Shell.FakeFindAsync(WithoutAnyForm);

		RemoteCommandResult result = await Shell.RunAsync(_site, Query("*"), firstOnPath: find);

		Assert.Equal(RemoteScripts.ExitUnsupported, result.ExitStatus);
		Assert.Equal(RemoteFileErrorKind.NotSupported, RemoteCommandErrors.ToException(result.ExitStatus, result.Error, _site).Kind);
	}

	[Fact]
	public async Task Search_OfAMissingFolderOrAFile_FailsTheWayAListingDoes()
	{
		RemoteCommandResult missing = await Shell.RunAsync(_site + "/nope", Query("*"));
		RemoteCommandResult file = await Shell.RunAsync(_site + "/index.html", Query("*"));

		Assert.Equal(RemoteScripts.ExitNotFound, missing.ExitStatus);
		Assert.Equal(RemoteScripts.ExitWrongKind, file.ExitStatus);
	}

	// Everything the query matches comes back whatever find could say of it, and nothing else does: what one search of
	// every name (hidden included) lists, held to the query here, is what the query's own search returns.
	[Theory]
	[MemberData(nameof(Queries))]
	public async Task Search_ReturnsExactlyWhatTheQueryMatches(string pattern, bool matchCase, bool includeHidden, RemoteSearchKinds kinds)
	{
		RemoteSearchQuery query = new() { Pattern = pattern, MatchCase = matchCase, IncludeHidden = includeHidden, Kinds = kinds };
		ServerSearchResult everything = await Shell.SearchAsync(_site, Query("") with { IncludeHidden = true });

		ServerSearchResult result = await Shell.SearchAsync(_site, query);

		List<string> expected = [.. Entries(everything)
			.Where(entry => query.Matches(entry) && (includeHidden || !InsideHidden(entry)))
			.Select(entry => RemotePath.GetRelativePath(_site, entry.Path)!)
			.Order(StringComparer.Ordinal)];
		Assert.Equal(expected, Relative(result));
	}

	private static RemoteSearchQuery Query(string pattern) => new() { Pattern = pattern };

	private static List<RemoteFileEntry> Entries(ServerSearchResult result) => [.. result.Steps.Select(step => step.Entry).OfType<RemoteFileEntry>()];

	private List<string> Relative(ServerSearchResult result) =>
		[.. Entries(result).Select(entry => RemotePath.GetRelativePath(_site, entry.Path)!).Order(StringComparer.Ordinal)];

	private bool InsideHidden(RemoteFileEntry entry) =>
		RemotePath.GetRelativePath(_site, RemotePath.GetParent(entry.Path))!.Split('/').Any(part => part.StartsWith('.'));

	private static List<RemoteFileEntry> WholeSeconds(ServerSearchResult result) =>
	[
		.. Entries(result)
			.Select(entry => entry with { LastModified = entry.LastModified is { } time ? time.AddTicks(-(time.Ticks % TimeSpan.TicksPerSecond)) : null })
			.OrderBy(entry => entry.Path, StringComparer.Ordinal),
	];
}
