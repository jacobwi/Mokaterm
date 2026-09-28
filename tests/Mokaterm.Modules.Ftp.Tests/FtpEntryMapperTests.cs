using FluentFTP;
using FluentFTP.Helpers;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Modules.Ftp.FileSystem;

namespace Mokaterm.Modules.Ftp.Tests;

public sealed class FtpEntryMapperTests
{
	[Fact]
	public void ToEntry_File_MapsSizeTimeOwnerAndMode()
	{
		FtpListItem item = new()
		{
			Name = "notes.txt",
			Type = FtpObjectType.File,
			Size = 1234,
			Modified = new DateTime(2026, 3, 14, 9, 26, 11, DateTimeKind.Utc),
			Chmod = 644,
			OwnerPermissions = FtpPermission.Read | FtpPermission.Write,
			GroupPermissions = FtpPermission.Read,
			OthersPermissions = FtpPermission.Read,
			RawOwner = "alice",
			RawGroup = "staff",
		};

		RemoteFileEntry entry = FtpEntryMapper.ToEntry(item, "/home/alice/notes.txt");

		Assert.Equal("notes.txt", entry.Name);
		Assert.Equal("/home/alice/notes.txt", entry.Path);
		Assert.Equal(RemoteEntryKind.File, entry.Kind);
		Assert.Equal(1234, entry.Size);
		Assert.Equal(new DateTimeOffset(2026, 3, 14, 9, 26, 11, TimeSpan.Zero), entry.LastModified);
		Assert.Equal(Octal("644"), entry.Permissions);
		Assert.Equal("alice", entry.Owner);
		Assert.Equal("staff", entry.Group);
		Assert.Null(entry.LinkTarget);
		Assert.Null(entry.LinkTargetKind);
	}

	[Fact]
	public void ToEntry_Directory_ClampsUnknownSizeAndReadsZonelessTimeAsUtc()
	{
		FtpListItem item = new()
		{
			Name = "www",
			Type = FtpObjectType.Directory,
			Size = -1,
			Modified = new DateTime(2024, 1, 3, 12, 0, 0, DateTimeKind.Unspecified),
			Chmod = 755,
		};

		RemoteFileEntry entry = FtpEntryMapper.ToEntry(item, "/home/alice//www/");

		Assert.Equal("www", entry.Name);
		Assert.Equal("/home/alice/www", entry.Path);
		Assert.Equal(RemoteEntryKind.Directory, entry.Kind);
		Assert.Equal(0, entry.Size);
		Assert.Equal(new DateTimeOffset(2024, 1, 3, 12, 0, 0, TimeSpan.Zero), entry.LastModified);
		Assert.Equal(Octal("755"), entry.Permissions);
		Assert.True(entry.IsDirectoryLike);
	}

	[Fact]
	public void ToEntry_Link_KeepsTargetAndResolvedKind()
	{
		FtpListItem item = new()
		{
			Name = "site",
			Type = FtpObjectType.Link,
			LinkTarget = "/var/www",
			LinkObject = new FtpListItem { Name = "www", Type = FtpObjectType.Directory },
			Chmod = 777,
		};

		RemoteFileEntry entry = FtpEntryMapper.ToEntry(item, "/home/alice/site");

		Assert.Equal(RemoteEntryKind.SymbolicLink, entry.Kind);
		Assert.Equal("/var/www", entry.LinkTarget);
		Assert.Equal(RemoteEntryKind.Directory, entry.LinkTargetKind);
		Assert.True(entry.IsDirectoryLike);
	}

	[Fact]
	public void ToEntry_LinkWithoutResolvedTarget_LeavesTargetKindUnknown()
	{
		FtpListItem item = new() { Name = "current", Type = FtpObjectType.Link };

		RemoteFileEntry entry = FtpEntryMapper.ToEntry(item, "/srv/current");

		Assert.Null(entry.LinkTarget);
		Assert.Null(entry.LinkTargetKind);
		Assert.False(entry.IsDirectoryLike);
	}

	[Fact]
	public void ToEntry_NonLinkWithTargetText_DropsTarget()
	{
		FtpListItem item = new() { Name = "file", Type = FtpObjectType.File, LinkTarget = "leftover" };

		Assert.Null(FtpEntryMapper.ToEntry(item, "/file").LinkTarget);
	}

	[Theory]
	[InlineData(644, FtpSpecialPermissions.None, "644")]
	[InlineData(755, FtpSpecialPermissions.SetUserID, "4755")]
	[InlineData(775, FtpSpecialPermissions.SetGroupID, "2775")]
	[InlineData(777, FtpSpecialPermissions.Sticky, "1777")]
	[InlineData(755, FtpSpecialPermissions.SetUserID | FtpSpecialPermissions.SetGroupID | FtpSpecialPermissions.Sticky, "7755")]
	[InlineData(4755, FtpSpecialPermissions.None, "4755")]
	[InlineData(7, FtpSpecialPermissions.None, "007")]
	public void ToPermissions_CombinesChmodDigitsAndSpecialBits(int chmod, FtpSpecialPermissions special, string expectedOctal)
	{
		FtpListItem item = new() { Chmod = chmod, SpecialPermissions = special };

		Assert.Equal(Octal(expectedOctal), FtpEntryMapper.ToPermissions(item));
	}

	[Fact]
	public void ToPermissions_WithoutChmod_UsesPermissionFlags()
	{
		FtpListItem item = new()
		{
			OwnerPermissions = FtpPermission.Read | FtpPermission.Write | FtpPermission.Execute,
			GroupPermissions = FtpPermission.Read | FtpPermission.Execute,
			OthersPermissions = FtpPermission.None,
		};

		Assert.Equal(Octal("750"), FtpEntryMapper.ToPermissions(item));
	}

	[Fact]
	public void ToPermissions_ModeStringWithNoBits_IsModeZero()
	{
		FtpListItem item = new() { RawPermissions = "----------" };

		Assert.Equal(UnixFileMode.None, FtpEntryMapper.ToPermissions(item));
	}

	[Fact]
	public void ToPermissions_ListingWithoutPermissions_IsNull()
	{
		FtpListItem item = new() { Name = "report.docx", Type = FtpObjectType.File, RawPermissions = "" };

		Assert.Null(FtpEntryMapper.ToPermissions(item));
	}

	[Theory]
	[InlineData(789)]
	[InlineData(8888)]
	[InlineData(-1)]
	public void TryParseChmod_RejectsNonOctalValues(int chmod) => Assert.False(FtpEntryMapper.TryParseChmod(chmod, out _));

	[Fact]
	public void ToTimestamp_MissingTimeIsNull() => Assert.Null(FtpEntryMapper.ToTimestamp(DateTime.MinValue));

	[Fact]
	public void ToTimestamp_LocalTimeKeepsItsOffset()
	{
		DateTime local = new(2026, 6, 1, 8, 30, 0, DateTimeKind.Local);

		Assert.Equal(new DateTimeOffset(local), FtpEntryMapper.ToTimestamp(local));
	}

	[Theory]
	[InlineData("-rw-r--r--    1 alice    staff        1234 Mar 14  2025 notes.txt", "notes.txt", RemoteEntryKind.File, 1234, "644")]
	[InlineData("drwxr-xr-x    2 alice    staff        4096 Jan  3  2024 www", "www", RemoteEntryKind.Directory, 4096, "755")]
	[InlineData("lrwxrwxrwx    1 root     root           11 Sep 17  2025 current -> releases/42", "current", RemoteEntryKind.SymbolicLink, 11, "777")]
	[InlineData("-rwsr-sr-t    1 root     wheel       99999 Feb 29  2020 special.bin", "special.bin", RemoteEntryKind.File, 99999, "7755")]
	[InlineData("----------    1 bob      bob             0 Jul  4  2025 locked", "locked", RemoteEntryKind.File, 0, "000")]
	[InlineData("-rw-r--r--+   1 alice    staff        1234 Mar 14  2025 acl.txt", "acl.txt", RemoteEntryKind.File, 1234, "644")]
	public void ToEntry_UnixListLine_ParsedByFluentFtp(string line, string name, RemoteEntryKind kind, long size, string octal)
	{
		FtpListItem item = Parse(line, FtpParser.Unix, machineList: false);

		RemoteFileEntry entry = FtpEntryMapper.ToEntry(item, "/home/alice/" + item.Name);

		Assert.Equal(name, entry.Name);
		Assert.Equal(kind, entry.Kind);
		Assert.Equal(size, entry.Size);
		Assert.Equal(Octal(octal), entry.Permissions);
		Assert.NotNull(entry.LastModified);
	}

	[Fact]
	public void ToEntry_UnixLinkLine_ResolvesRelativeTargetAgainstFolder()
	{
		FtpListItem item = Parse("lrwxrwxrwx    1 root     root           11 Sep 17  2025 current -> releases/42", FtpParser.Unix, machineList: false);

		RemoteFileEntry entry = FtpEntryMapper.ToEntry(item, "/home/alice/current");

		Assert.Equal("/home/alice/releases/42", entry.LinkTarget);
		Assert.Equal("root", entry.Owner);
	}

	[Fact]
	public void ToEntry_MachineListing_ReadsUtcTimeAndMode()
	{
		FtpListItem item = Parse("type=file;size=1234;modify=20260314092611;UNIX.mode=0644; notes.txt", FtpParser.Machine, machineList: true);

		RemoteFileEntry entry = FtpEntryMapper.ToEntry(item, "/home/alice/notes.txt");

		Assert.Equal(RemoteEntryKind.File, entry.Kind);
		Assert.Equal(1234, entry.Size);
		Assert.Equal(new DateTimeOffset(2026, 3, 14, 9, 26, 11, TimeSpan.Zero), entry.LastModified);
		Assert.Equal(Octal("644"), entry.Permissions);
	}

	[Theory]
	[InlineData("type=cdir;modify=20260917100000;perm=el; .")]
	[InlineData("type=pdir;modify=20260917100000;perm=el; ..")]
	public void IsListable_SkipsSelfAndParentEntries(string line) =>
		Assert.False(FtpEntryMapper.IsListable(Parse(line, FtpParser.Machine, machineList: true)));

	[Fact]
	public void IsListable_AcceptsHiddenFiles() =>
		Assert.True(FtpEntryMapper.IsListable(new FtpListItem { Name = ".profile", Type = FtpObjectType.File }));

	private static UnixFileMode Octal(string digits)
	{
		Assert.True(UnixFileModeFormat.TryParseOctal(digits, out UnixFileMode mode));
		return mode;
	}

	private static FtpListItem Parse(string line, FtpParser parser, bool machineList)
	{
		using AsyncFtpClient client = new();
		FtpListParser listParser = new(client);
		listParser.Init(FtpOperatingSystem.Unix, parser);
		FtpListItem? item = listParser.ParseSingleLine("/home/alice", line, [], machineList);
		Assert.NotNull(item);
		return item;
	}
}
