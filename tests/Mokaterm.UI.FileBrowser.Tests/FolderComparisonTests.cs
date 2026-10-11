using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Platform;
using Mokaterm.UI.FileBrowser.Compare;

namespace Mokaterm.UI.FileBrowser.Tests;

public sealed class FolderComparisonTests
{
	private const string Root = "/srv/site";

	private static readonly DateTimeOffset Noon = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

	[Fact]
	public void LocalFiles_DropThePickedFolderNameAndTheFolders()
	{
		Dictionary<string, LocalFileItem> files = FolderComparison.LocalFiles(
		[
			LocalFolder("site"),
			LocalFolder("site/css"),
			Local("site/index.html"),
			Local("site/css/app.css"),
		]);

		Assert.Equal(["css/app.css", "index.html"], files.Keys.Order(StringComparer.Ordinal));
	}

	[Fact]
	public void LocalFiles_KeepTheSegmentsTheUploadKeeps()
	{
		Dictionary<string, LocalFileItem> files = FolderComparison.LocalFiles([Local("site/./css//../app.css"), Local("lonely.txt")]);

		Assert.Equal(["css/app.css", "lonely.txt"], files.Keys.Order(StringComparer.Ordinal));
	}

	[Fact]
	public void FolderName_IsTheFirstSegment()
	{
		Assert.Equal("site", FolderComparison.FolderName([LocalFolder("site"), Local("site/index.html")]));
		Assert.Equal("", FolderComparison.FolderName([]));
	}

	[Fact]
	public void RemoteFiles_AreKeyedBelowTheRootAndLeaveFoldersOut()
	{
		Dictionary<string, RemoteFileEntry> files = FolderComparison.RemoteFiles(Root,
		[
			Remote("/srv/site/index.html"),
			Remote("/srv/site/css/app.css"),
			new RemoteFileEntry { Name = "css", Path = "/srv/site/css", Kind = RemoteEntryKind.Directory },
			new RemoteFileEntry { Name = "latest", Path = "/srv/site/latest", Kind = RemoteEntryKind.SymbolicLink, LinkTargetKind = RemoteEntryKind.Directory },
			new RemoteFileEntry { Name = "current", Path = "/srv/site/current", Kind = RemoteEntryKind.SymbolicLink, LinkTargetKind = RemoteEntryKind.File },
		]);

		Assert.Equal(["css/app.css", "current", "index.html"], files.Keys.Order(StringComparer.Ordinal));
	}

	[Fact]
	public void Classify_OneSideOnly()
	{
		Assert.Equal(FolderCompareStatus.OnlyLocal, Classify(Local("site/a"), null));
		Assert.Equal(FolderCompareStatus.OnlyRemote, Classify(null, Remote("/srv/site/a")));
	}

	[Fact]
	public void Classify_SameTimeAndSize_IsSame() =>
		Assert.Equal(FolderCompareStatus.Same, Classify(Local("site/a", 10, Noon), Remote("/srv/site/a", 10, Noon)));

	[Fact]
	public void Classify_NewerSide_WinsWhateverTheSize()
	{
		Assert.Equal(FolderCompareStatus.LocalNewer, Classify(Local("site/a", 10, Noon.AddMinutes(5)), Remote("/srv/site/a", 10, Noon)));
		Assert.Equal(FolderCompareStatus.RemoteNewer, Classify(Local("site/a", 10, Noon), Remote("/srv/site/a", 99, Noon.AddMinutes(5))));
	}

	[Fact]
	public void Classify_SameTimeOtherSize_IsSizeDiffers() =>
		Assert.Equal(FolderCompareStatus.SizeDiffers, Classify(Local("site/a", 10, Noon), Remote("/srv/site/a", 11, Noon)));

	// The server keeps whole seconds and the local copy carries milliseconds, or a FAT drive two-second steps.
	[Theory]
	[InlineData(2000, nameof(FolderCompareStatus.Same))]
	[InlineData(-2000, nameof(FolderCompareStatus.Same))]
	[InlineData(2001, nameof(FolderCompareStatus.LocalNewer))]
	[InlineData(-2001, nameof(FolderCompareStatus.RemoteNewer))]
	public void Classify_TimesWithinTheTolerance_AreTheSame(int localAheadMilliseconds, string expected) =>
		Assert.Equal(Enum.Parse<FolderCompareStatus>(expected), Classify(Local("site/a", 10, Noon.AddMilliseconds(localAheadMilliseconds)), Remote("/srv/site/a", 10, Noon)));

	[Fact]
	public void Classify_ToleranceCanBeWidened()
	{
		FolderCompareOptions options = FolderCompareOptions.Default with { TimeTolerance = TimeSpan.FromHours(1) };

		Assert.Equal(
			FolderCompareStatus.Same,
			FolderComparison.Classify(Local("site/a", 10, Noon.AddMinutes(59)), Remote("/srv/site/a", 10, Noon), options));
	}

	[Fact]
	public void Classify_IgnoringTimes_ComparesSizesOnly()
	{
		FolderCompareOptions sizesOnly = FolderCompareOptions.Default with { IgnoreTimes = true };

		Assert.Equal(FolderCompareStatus.Same, FolderComparison.Classify(Local("site/a", 10, Noon.AddDays(3)), Remote("/srv/site/a", 10, Noon), sizesOnly));
		Assert.Equal(FolderCompareStatus.SizeDiffers, FolderComparison.Classify(Local("site/a", 10, Noon), Remote("/srv/site/a", 12, Noon.AddDays(3)), sizesOnly));
	}

	[Fact]
	public void Classify_AnUnknownTimeOrSize_ComparesWhatIsKnown()
	{
		Assert.Equal(FolderCompareStatus.SizeDiffers, Classify(Local("site/a", 10, modified: null), Remote("/srv/site/a", 11, Noon)));
		Assert.Equal(FolderCompareStatus.Same, Classify(Local("site/a", 10, Noon), Remote("/srv/site/a", 10, modified: null)));
		Assert.Equal(FolderCompareStatus.Same, Classify(Local("site/a", length: null, Noon), Remote("/srv/site/a", 10, Noon)));
	}

	[Fact]
	public void Compare_LinesNestedPathsUpInPathOrder()
	{
		List<FolderCompareRow> rows = Compare(
			[Local("site/index.html", 5, Noon), Local("site/css/app.css", 7, Noon.AddMinutes(1)), Local("site/img/new.png", 1, Noon)],
			[Remote("/srv/site/index.html", 5, Noon), Remote("/srv/site/css/app.css", 7, Noon), Remote("/srv/site/old/gone.txt", 1, Noon)]);

		Assert.Equal(
			[
				("css/app.css", FolderCompareStatus.LocalNewer),
				("img/new.png", FolderCompareStatus.OnlyLocal),
				("index.html", FolderCompareStatus.Same),
				("old/gone.txt", FolderCompareStatus.OnlyRemote),
			],
			rows.Select(row => (row.RelativePath, row.Status)));
	}

	// The server is POSIX: README.md beside Readme.md is a second file there, and the upload writes the local name.
	[Fact]
	public void Compare_PathsDifferingInCase_AreDifferentFiles()
	{
		List<FolderCompareRow> rows = Compare([Local("site/Readme.md", 5, Noon)], [Remote("/srv/site/README.md", 5, Noon)]);

		Assert.Equal(
			[("README.md", FolderCompareStatus.OnlyRemote), ("Readme.md", FolderCompareStatus.OnlyLocal)],
			rows.Select(row => (row.RelativePath, row.Status)));
	}

	[Theory]
	[InlineData(nameof(FolderCompareStatus.OnlyLocal), true)]
	[InlineData(nameof(FolderCompareStatus.LocalNewer), true)]
	[InlineData(nameof(FolderCompareStatus.SizeDiffers), true)]
	[InlineData(nameof(FolderCompareStatus.RemoteNewer), false)]
	[InlineData(nameof(FolderCompareStatus.Same), false)]
	public void Preselection_TicksWhatTheServerLacksOrHasOlder(string name, bool expected)
	{
		FolderCompareStatus status = Enum.Parse<FolderCompareStatus>(name);
		FolderCompareRow row = new("a", status, Local("site/a", 10, Noon), status == FolderCompareStatus.OnlyLocal ? null : Remote("/srv/site/a", 11, Noon));

		Assert.Equal(expected, FolderComparison.IsPreselected(row, FolderCompareOptions.Default, remoteComplete: true));
	}

	[Fact]
	public void Preselection_NeverTicksAFileOnlyOnTheServer()
	{
		FolderCompareRow row = new("a", FolderCompareStatus.OnlyRemote, null, Remote("/srv/site/a"));

		Assert.False(FolderComparison.IsPreselected(row, FolderCompareOptions.Default, remoteComplete: true));
	}

	// The walk stopped at a cap or could not read a folder, so "only local" may only mean "not seen".
	[Fact]
	public void Preselection_LeavesOnlyLocalUntickedWhenTheServerSideIsIncomplete()
	{
		FolderCompareRow onlyLocal = new("a", FolderCompareStatus.OnlyLocal, Local("site/a"), null);
		FolderCompareRow newer = new("b", FolderCompareStatus.LocalNewer, Local("site/b", 1, Noon.AddDays(1)), Remote("/srv/site/b", 1, Noon));

		Assert.False(FolderComparison.IsPreselected(onlyLocal, FolderCompareOptions.Default, remoteComplete: false));
		Assert.True(FolderComparison.IsPreselected(newer, FolderCompareOptions.Default, remoteComplete: false));
	}

	[Fact]
	public void Preselection_ComparingSizesOnly_StillLeavesAnOlderLocalCopyUnticked()
	{
		FolderCompareOptions sizesOnly = FolderCompareOptions.Default with { IgnoreTimes = true };
		List<FolderCompareRow> rows = FolderComparison.Compare(
			FolderComparison.LocalFiles([Local("site/older", 10, Noon), Local("site/newer", 10, Noon.AddDays(1)), Local("site/unknown", 10, modified: null)]),
			FolderComparison.RemoteFiles(Root, [Remote("/srv/site/older", 11, Noon.AddDays(1)), Remote("/srv/site/newer", 11, Noon), Remote("/srv/site/unknown", 11, Noon)]),
			sizesOnly);

		Assert.All(rows, row => Assert.Equal(FolderCompareStatus.SizeDiffers, row.Status));
		Assert.Equal(
			["newer", "unknown"],
			rows.Where(row => FolderComparison.IsPreselected(row, sizesOnly, remoteComplete: true)).Select(row => row.RelativePath));
	}

	[Fact]
	public void UploadItems_AreRenamedBelowTheRemoteFolder()
	{
		LocalFileItem picked = Local("site/css/app.css", 7, Noon);
		List<FolderCompareRow> rows =
		[
			new("css/app.css", FolderCompareStatus.LocalNewer, picked, Remote("/srv/site/css/app.css")),
			new("old.txt", FolderCompareStatus.OnlyRemote, null, Remote("/srv/site/old.txt")),
		];

		LocalFileItem item = Assert.Single(FolderComparison.UploadItems(rows));

		Assert.Equal("css/app.css", item.RelativePath);
		Assert.Equal("app.css", item.Name);
		Assert.Same(picked.OpenReadAsync, item.OpenReadAsync);
		Assert.Equal(7L, item.Length);
	}

	// A file touched or copied without a change differs only in its time, which is what a content check settles.
	[Fact]
	public void ContentCandidates_AreTheRowsWhoseSizesMatchAndOnlyTheTimesDiffer()
	{
		List<FolderCompareRow> rows = Compare(
			[
				Local("site/touched.txt", 10, Noon.AddHours(1)),
				Local("site/older.txt", 10, Noon),
				Local("site/edited.txt", 12, Noon.AddHours(1)),
				Local("site/same.txt", 10, Noon),
				Local("site/new.txt", 10, Noon),
			],
			[
				Remote("/srv/site/touched.txt", 10, Noon),
				Remote("/srv/site/older.txt", 10, Noon.AddHours(1)),
				Remote("/srv/site/edited.txt", 10, Noon),
				Remote("/srv/site/same.txt", 10, Noon),
			]);

		Assert.Equal(["older.txt", "touched.txt"], FolderComparison.ContentCandidates(rows).Select(row => row.RelativePath));
	}

	[Fact]
	public void SameContent_MakesARowTheSameWhateverItsTimes()
	{
		FolderCompareOptions options = FolderCompareOptions.Default with { SameContent = new HashSet<string>(StringComparer.Ordinal) { "touched.txt" } };

		List<FolderCompareRow> rows = FolderComparison.Compare(
			FolderComparison.LocalFiles([Local("site/touched.txt", 10, Noon.AddHours(1)), Local("site/other.txt", 10, Noon.AddHours(1))]),
			FolderComparison.RemoteFiles(Root, [Remote("/srv/site/touched.txt", 10, Noon), Remote("/srv/site/other.txt", 10, Noon)]),
			options);

		Assert.Equal(FolderCompareStatus.LocalNewer, rows.Single(row => row.RelativePath == "other.txt").Status);
		FolderCompareRow touched = rows.Single(row => row.RelativePath == "touched.txt");
		Assert.Equal(FolderCompareStatus.Same, touched.Status);
		Assert.False(FolderComparison.IsPreselected(touched, options, remoteComplete: true));
	}

	[Fact]
	public void ContentCandidates_StopAtTheCap()
	{
		LocalFileItem[] local = [.. Enumerable.Range(0, FolderComparison.MaxContentChecks + 20).Select(index => Local($"site/f{index}", 10, Noon.AddHours(1)))];
		RemoteFileEntry[] remote = [.. Enumerable.Range(0, FolderComparison.MaxContentChecks + 20).Select(index => Remote($"/srv/site/f{index}", 10, Noon))];

		Assert.Equal(FolderComparison.MaxContentChecks, FolderComparison.ContentCandidates(Compare(local, remote)).Count);
	}

	private static FolderCompareStatus Classify(LocalFileItem? local, RemoteFileEntry? remote) =>
		FolderComparison.Classify(local, remote, FolderCompareOptions.Default);

	private static List<FolderCompareRow> Compare(LocalFileItem[] local, RemoteFileEntry[] remote) =>
		FolderComparison.Compare(FolderComparison.LocalFiles(local), FolderComparison.RemoteFiles(Root, remote), FolderCompareOptions.Default);

	private static LocalFileItem Local(string relativePath, long? length = 10, DateTimeOffset? modified = null) => new()
	{
		Name = relativePath[(relativePath.LastIndexOf('/') + 1)..],
		RelativePath = relativePath,
		Length = length,
		LastModified = modified,
		OpenReadAsync = _ => ValueTask.FromResult<Stream>(new MemoryStream([1, 2, 3])),
	};

	private static LocalFileItem LocalFolder(string relativePath) => new()
	{
		Name = relativePath[(relativePath.LastIndexOf('/') + 1)..],
		RelativePath = relativePath,
		IsDirectory = true,
	};

	private static RemoteFileEntry Remote(string path, long size = 10, DateTimeOffset? modified = null) => new()
	{
		Name = RemotePath.GetName(path),
		Path = path,
		Kind = RemoteEntryKind.File,
		Size = size,
		LastModified = modified,
	};
}
