using System.Text;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Modules.Ssh.Shell;

namespace Mokaterm.Modules.Ssh.Tests;

public sealed class RemoteListingParserTests
{
	[Fact]
	public void Parse_GnuListing_MapsEveryField()
	{
		byte[] payload = Fields(
			"G",
			"f", "1204", "1694959123.2500000000", "644", "www-data", "adm", "", "", "access.log",
			"d", "4096", "1694959000.0000000000", "2775", "root", "root", "", "", "sites-enabled",
			"l", "11", "1694950000.5000000000", "777", "root", "root", "../conf.d", "d", "conf",
			"l", "7", "1694950000.0000000000", "777", "root", "root", "missing", "N", "dangling");

		List<RemoteFileEntry> entries = RemoteListingParser.Parse(payload, "/etc/nginx", isStat: false);

		Assert.Equal(4, entries.Count);

		RemoteFileEntry file = entries[0];
		Assert.Equal("access.log", file.Name);
		Assert.Equal("/etc/nginx/access.log", file.Path);
		Assert.Equal(RemoteEntryKind.File, file.Kind);
		Assert.Equal(1204, file.Size);
		Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1694959123).AddMilliseconds(250), file.LastModified);
		Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead, file.Permissions);
		Assert.Equal("www-data", file.Owner);
		Assert.Equal("adm", file.Group);
		Assert.Null(file.LinkTarget);
		Assert.Null(file.LinkTargetKind);

		RemoteFileEntry directory = entries[1];
		Assert.Equal(RemoteEntryKind.Directory, directory.Kind);
		Assert.Equal("2775", UnixFileModeFormat.ToOctal(directory.Permissions!.Value));

		RemoteFileEntry link = entries[2];
		Assert.Equal(RemoteEntryKind.SymbolicLink, link.Kind);
		Assert.Equal("../conf.d", link.LinkTarget);
		Assert.Equal(RemoteEntryKind.Directory, link.LinkTargetKind);
		Assert.True(link.IsDirectoryLike);

		RemoteFileEntry dangling = entries[3];
		Assert.Equal("missing", dangling.LinkTarget);
		Assert.Null(dangling.LinkTargetKind);
		Assert.False(dangling.IsDirectoryLike);
	}

	[Fact]
	public void Parse_NamesWithTabsNewlinesAndUnicode_StayIntact()
	{
		byte[] payload = Fields(
			"G",
			"f", "1", "1700000000", "600", "abc", "abc", "", "", "tab\there",
			"f", "2", "1700000000", "600", "abc", "abc", "", "", "line\nbreak",
			"f", "3", "1700000000", "600", "abc", "abc", "", "", "résumé 日本.txt");

		List<RemoteFileEntry> entries = RemoteListingParser.Parse(payload, "/home/abc", isStat: false);

		Assert.Equal(["tab\there", "line\nbreak", "résumé 日本.txt"], entries.Select(entry => entry.Name));
		Assert.Equal("/home/abc/line\nbreak", entries[1].Path);
		Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000), entries[0].LastModified);
	}

	[Fact]
	public void Parse_BusyBoxListing_MapsRawModesAndFallsBackToIds()
	{
		byte[] payload = Fields(
			"P",
			"81a4 512 1700000100 0 0 root root", "", "", "hosts",
			"41ed 4096 1700000200 1000 1000 UNKNOWN UNKNOWN", "", "", "data",
			"a1ff 12 1700000300 0 0 root root", "/proc/self/fd", "d", "fd",
			"a1ff 7 1700000400 0 0 root root", "nowhere", "N", "broken",
			"81ed 2048 1700000500 0 0 root root", "", "", "file with\ttab");

		List<RemoteFileEntry> entries = RemoteListingParser.Parse(payload, "/", isStat: false);

		Assert.Equal(5, entries.Count);
		Assert.Equal(RemoteEntryKind.File, entries[0].Kind);
		Assert.Equal("/hosts", entries[0].Path);
		Assert.Equal("644", UnixFileModeFormat.ToOctal(entries[0].Permissions!.Value));
		Assert.Equal(512, entries[0].Size);
		Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000100), entries[0].LastModified);

		Assert.Equal(RemoteEntryKind.Directory, entries[1].Kind);
		Assert.Equal("1000", entries[1].Owner);
		Assert.Equal("1000", entries[1].Group);

		Assert.Equal(RemoteEntryKind.SymbolicLink, entries[2].Kind);
		Assert.Equal("/proc/self/fd", entries[2].LinkTarget);
		Assert.Equal(RemoteEntryKind.Directory, entries[2].LinkTargetKind);

		Assert.Null(entries[3].LinkTargetKind);
		Assert.Equal("755", UnixFileModeFormat.ToOctal(entries[4].Permissions!.Value));
		Assert.Equal("file with\ttab", entries[4].Name);
	}

	[Fact]
	public void Parse_Stat_UsesThePathAskedFor()
	{
		byte[] payload = Fields("G", "l", "9", "1700000000.1234567890", "777", "root", "root", "/var/run", "d", "/run");

		RemoteFileEntry entry = Assert.Single(RemoteListingParser.Parse(payload, "/run/", isStat: true));

		Assert.Equal("run", entry.Name);
		Assert.Equal("/run", entry.Path);
		Assert.Equal(RemoteEntryKind.SymbolicLink, entry.Kind);
		Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000).AddTicks(1234567), entry.LastModified);
	}

	[Fact]
	public void Parse_SkipsMalformedAndIncompleteRecords()
	{
		byte[] complete = Fields(
			"G",
			"f", "not-a-size", "1700000000", "644", "a", "a", "", "", "bad-size",
			"f", "5", "1700000000", "999", "a", "a", "", "", "bad-mode",
			"f", "5", "1700000000", "644", "a", "a", "", "", "..",
			"f", "5", "1700000000", "644", "a", "a", "", "", "good");
		byte[] truncated = [.. complete, .. "f\012\0"u8];

		List<RemoteFileEntry> entries = RemoteListingParser.Parse(truncated, "/srv", isStat: false);

		RemoteFileEntry entry = Assert.Single(entries);
		Assert.Equal("good", entry.Name);
	}

	[Theory]
	[InlineData("")]
	[InlineData("X")]
	public void Parse_UnknownFormatTag_ReturnsNothing(string tag) =>
		Assert.Empty(RemoteListingParser.Parse(Fields(tag, "f", "1", "1", "644", "a", "a", "", "", "x"), "/", isStat: false));

	[Fact]
	public void TryGetPayload_SkipsLoginNoiseBeforeTheMarker()
	{
		byte[] output = [.. "Welcome to host!\n"u8, .. RemoteOutput.Marker, .. "G\0"u8];

		Assert.True(RemoteOutput.TryGetPayload(output, out ReadOnlyMemory<byte> payload));
		Assert.Equal(["G"], RemoteOutput.SplitFields(payload.Span));
		Assert.False(RemoteOutput.TryGetPayload("sudo: a password is required\n"u8.ToArray(), out _));
	}

	[Theory]
	[InlineData("1700000000", 0)]
	[InlineData("1700000000.5", 5_000_000)]
	[InlineData("1700000000.0000001", 1)]
	[InlineData("1700000000.123456789", 1_234_567)]
	public void ParseEpoch_ReadsFractionsToTicks(string value, long ticks) =>
		Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000).AddTicks(ticks), RemoteListingParser.ParseEpoch(value));

	[Theory]
	[InlineData("")]
	[InlineData("soon")]
	[InlineData("99999999999999")]
	public void ParseEpoch_RejectsGarbage(string value) => Assert.Null(RemoteListingParser.ParseEpoch(value));

	private static byte[] Fields(params string[] fields)
	{
		StringBuilder builder = new();
		foreach (string field in fields)
		{
			builder.Append(field).Append('\0');
		}

		return Encoding.UTF8.GetBytes(builder.ToString());
	}
}
