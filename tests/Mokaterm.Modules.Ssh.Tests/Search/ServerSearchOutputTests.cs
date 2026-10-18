using System.Text;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Modules.Ssh.Shell;

namespace Mokaterm.Modules.Ssh.Tests.Search;

/// <summary>What the search script prints, canned in both of its forms, and how it reads back against the query's caps.</summary>
public sealed class ServerSearchOutputTests
{
	private const string Root = "/srv/site";

	[Fact]
	public void Gnu_RecordsReadBackIntoEntriesUnderTheRoot()
	{
		byte[] payload = Gnu(
			Folder("./logs"),
			GnuMatch("f", 120, "1694959123.2500000000", "640", "www", "adm", "", "", "./logs/access.log"),
			GnuMatch("l", 1, "1694959000", "777", "root", "root", ".", "d", "./loop"),
			GnuMatch("l", 7, "1694959000", "777", "root", "root", "nowhere", "N", "./broken"));

		ServerSearchResult result = Read(payload, Query("*"));

		List<RemoteFileEntry> entries = Entries(result);
		Assert.Equal(["/srv/site/logs/access.log", "/srv/site/loop", "/srv/site/broken"], entries.Select(entry => entry.Path));
		RemoteFileEntry log = entries[0];
		Assert.Equal("access.log", log.Name);
		Assert.Equal(RemoteEntryKind.File, log.Kind);
		Assert.Equal(120, log.Size);
		Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1694959123).AddMilliseconds(250), log.LastModified);
		Assert.Equal("640", UnixFileModeFormat.ToOctal(log.Permissions ?? UnixFileMode.None));
		Assert.Equal("www", log.Owner);
		Assert.Equal("adm", log.Group);
		Assert.Equal(RemoteEntryKind.Directory, entries[1].LinkTargetKind);
		Assert.Equal(".", entries[1].LinkTarget);
		Assert.Null(entries[2].LinkTargetKind);
		Assert.Equal(2, result.FoldersVisited);
		Assert.Equal(RemoteSearchLimit.None, result.Limit);
		Assert.Equal("/srv/site/logs", result.Steps[0].Folder);
	}

	[Fact]
	public void Posix_RecordsReadBackIntoTheSameEntries()
	{
		byte[] payload = Posix(
			Folder("./logs"),
			PosixMatch("81a0 120 1694959123 33 4 www adm", "", "", "./logs/access.log"),
			PosixMatch("a1ff 1 1694959000 0 0 root UNKNOWN", ".", "d", "./loop"));

		ServerSearchResult result = Read(payload, Query("*"));

		List<RemoteFileEntry> entries = Entries(result);
		Assert.Equal(RemoteEntryKind.File, entries[0].Kind);
		Assert.Equal(120, entries[0].Size);
		Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1694959123), entries[0].LastModified);
		Assert.Equal("640", UnixFileModeFormat.ToOctal(entries[0].Permissions ?? UnixFileMode.None));
		Assert.Equal(RemoteEntryKind.SymbolicLink, entries[1].Kind);
		Assert.Equal(RemoteEntryKind.Directory, entries[1].LinkTargetKind);
		Assert.Equal("0", entries[1].Group);
	}

	[Fact]
	public void TheRootFolder_PlacesPathsWithOneSlash()
	{
		ServerSearchResult result = Read(Gnu(GnuMatch("d", 0, "1", "755", "root", "root", "", "", "./etc")), Query("*"), "/");

		Assert.Equal("/etc", Assert.Single(Entries(result)).Path);
	}

	[Fact]
	public void EveryEntry_IsHeldToTheQuery_WhateverFindMatched()
	{
		byte[] payload = Gnu(
			GnuMatch("f", 1, "1", "644", "u", "g", "", "", "./Error.log"),
			GnuMatch("f", 1, "1", "644", "u", "g", "", "", "./error.txt"),
			GnuMatch("d", 0, "1", "755", "u", "g", "", "", "./error.d"),
			GnuMatch("f", 1, "1", "644", "u", "g", "", "", "./.error.log"));

		ServerSearchResult result = Read(payload, Query("error.???") with { MatchCase = true, Kinds = RemoteSearchKinds.Files });

		Assert.Equal(["/srv/site/error.txt"], Entries(result).Select(entry => entry.Path));
	}

	[Fact]
	public void TheResultCap_StopsAtTheCap_AndSaysSoWhenMoreFollowed()
	{
		byte[] payload = Gnu(File("./a.log"), File("./b.log"), File("./c.log"));

		ServerSearchResult cut = Read(payload, Query("*.log") with { MaxResults = 2 });
		ServerSearchResult exact = Read(payload, Query("*.log") with { MaxResults = 3 });

		Assert.Equal(2, Entries(cut).Count);
		Assert.Equal(RemoteSearchLimit.Results, cut.Limit);
		Assert.Equal(3, Entries(exact).Count);
		Assert.Equal(RemoteSearchLimit.None, exact.Limit);
	}

	[Fact]
	public void TheFolderCap_CountsTheRootAndStopsAtTheFolderPastIt()
	{
		byte[] payload = Gnu(Folder("./a"), File("./a/x.log"), Folder("./b"), File("./b/y.log"), Folder("./c"), File("./c/z.log"));

		ServerSearchResult result = Read(payload, Query("*.log") with { MaxFolders = 3 });

		Assert.Equal(["/srv/site/a/x.log", "/srv/site/b/y.log"], Entries(result).Select(entry => entry.Path));
		Assert.Equal(3, result.FoldersVisited);
		Assert.Equal(RemoteSearchLimit.Folders, result.Limit);
	}

	[Fact]
	public void AFolderAtTheDepthCap_IsNotEntered_AndSaysSo()
	{
		byte[] payload = Gnu(Folder("./a"), Folder("./a/b"), File("./a/b.log"));

		ServerSearchResult result = Read(payload, Query("*.log") with { MaxDepth = 2 });

		Assert.Equal(2, result.FoldersVisited);
		Assert.Equal(RemoteSearchLimit.Depth, result.Limit);
		Assert.Single(Entries(result));
	}

	[Fact]
	public void TheLastMatchTheCapAllows_WithDeeperFoldersLeft_IsCut()
	{
		byte[] payload = Gnu(Folder("./a"), Folder("./a/b"), File("./a/b.log"));

		ServerSearchResult result = Read(payload, Query("*.log") with { MaxDepth = 2, MaxResults = 1 });

		Assert.Equal(RemoteSearchLimit.Results, result.Limit);
	}

	[Fact]
	public void UnreadableFolders_ComeFromTheClosingCount()
	{
		ServerSearchResult result = Read(Gnu(unreadable: 2, records: [Folder("./a"), Folder("./b"), Folder("./c")]), Query("*"));

		Assert.Equal(2, result.UnreadableFolders);
		Assert.Equal(4, result.FoldersVisited);
	}

	[Fact]
	public void ARecordCutShort_IsLeftOut()
	{
		List<byte> payload = [.. Encoding.UTF8.GetBytes("G\0")];
		payload.AddRange(Encoding.UTF8.GetBytes(File("./a.log") + "Mf\0" + "1\0" + "17000"));
		payload.AddRange(Encoding.UTF8.GetBytes("\0U0\0E141\0"));

		ServerSearchResult result = Read([.. payload], Query("*"));

		Assert.Equal(["/srv/site/a.log"], Entries(result).Select(entry => entry.Path));
	}

	[Theory]
	[InlineData("")]
	[InlineData("X\0U0\0E0\0")]
	[InlineData("G\0D./a\0")]
	[InlineData("G\0D./a\0U0\0")]
	public void OutputThatIsNotTheScripts_IsNotRead(string output) =>
		Assert.Null(ServerSearchOutput.Read(Encoding.UTF8.GetBytes(output), Root, Query("*"), wholeSeconds: false));

	[Fact]
	public void PastTheByteBudget_WithNoCapReached_TheWalkHasToAnswer()
	{
		ServerSearchBudget budget = new(TimeSpan.FromMinutes(1), 40);
		byte[] payload = Gnu(File("./aaaaaaaa.txt"), File("./bbbbbbbb.txt"), File("./cccccccc.txt"));

		ServerSearchResult result = Read(payload, Query("?.log"), budget: budget);

		Assert.True(result.NeedsWalk);
	}

	[Fact]
	public void AFindStoppedByItsTimeLimit_KeepsWhatItFound_AndSaysSo()
	{
		ServerSearchResult result = Read(Gnu(status: "124", records: [File("./a.log")]), Query("*"));

		Assert.True(result.TimedOut);
		Assert.Single(Entries(result));
	}

	[Fact]
	public void WholeSeconds_DropTheFraction()
	{
		byte[] payload = Gnu(GnuMatch("f", 1, "1694959123.9", "644", "u", "g", "", "", "./a.log"));

		ServerSearchResult result = ServerSearchOutput.Read(payload, Root, Query("*"), wholeSeconds: true)!;

		Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1694959123), Assert.Single(Entries(result)).LastModified);
	}

	[Fact]
	public void ANameThatIsNotUtf8_ReadsTheWayEveryListingReadsIt()
	{
		List<byte> payload = [.. Encoding.UTF8.GetBytes("G\0Mf\0" + "1\0" + "1\0" + "644\0" + "u\0" + "g\0" + "\0" + "\0" + "./caf")];
		payload.Add(0xE9);
		payload.AddRange(Encoding.UTF8.GetBytes(".txt\0" + "\0U0\0E0\0"));

		ServerSearchResult result = Read([.. payload], Query("*"));

		Assert.Equal("caf" + (char)0xFFFD + ".txt", Assert.Single(Entries(result)).Name);
	}

	[Fact]
	public void AnEntryInAFolderNeverEntered_IsLeftOut()
	{
		byte[] payload = Gnu(File("./elsewhere/x.log"), File("./x.log"));

		ServerSearchResult result = Read(payload, Query("*"));

		Assert.Equal(["/srv/site/x.log"], Entries(result).Select(entry => entry.Path));
	}

	private static RemoteSearchQuery Query(string pattern) => new() { Pattern = pattern };

	private static ServerSearchResult Read(byte[] payload, RemoteSearchQuery query, string root = Root, ServerSearchBudget? budget = null) =>
		ServerSearchOutput.Read(payload, root, query, wholeSeconds: false, budget) ?? throw new InvalidOperationException("Not read.");

	private static List<RemoteFileEntry> Entries(ServerSearchResult result) => [.. result.Steps.Select(step => step.Entry).OfType<RemoteFileEntry>()];

	private static string Folder(string path) => "D" + path + "\0";

	private static string File(string path) => GnuMatch("f", 1, "1700000000", "644", "u", "g", "", "", path);

	private static string GnuMatch(string type, long size, string time, string mode, string user, string group, string target, string targetType, string path) =>
		string.Join('\0', "M" + type, size.ToString(System.Globalization.CultureInfo.InvariantCulture), time, mode, user, group, target, targetType, path) + "\0";

	private static string PosixMatch(string stat, string target, string targetType, string path) =>
		string.Join('\0', "M" + stat, target, targetType, path) + "\0";

	private static byte[] Gnu(params string[] records) => Gnu(0, "0", records);

	private static byte[] Gnu(int unreadable = 0, string status = "0", params string[] records) => Payload("G", unreadable, status, records);

	private static byte[] Posix(params string[] records) => Payload("P", 0, "0", records);

	private static byte[] Payload(string form, int unreadable, string status, string[] records) =>
		Encoding.UTF8.GetBytes(form + "\0" + string.Concat(records) + "\0U" + unreadable.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\0E" + status + "\0");
}
