using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.Core.Tests.Abstractions;

public sealed class RemoteArchivesTests
{
	[Theory]
	[InlineData("site.tar", RemoteArchiveKind.Tar)]
	[InlineData("site.tar.gz", RemoteArchiveKind.TarGzip)]
	[InlineData("site.TGZ", RemoteArchiveKind.TarGzip)]
	[InlineData("site.tar.bz2", RemoteArchiveKind.TarBzip2)]
	[InlineData("site.tbz2", RemoteArchiveKind.TarBzip2)]
	[InlineData("site.tar.xz", RemoteArchiveKind.TarXz)]
	[InlineData("site.txz", RemoteArchiveKind.TarXz)]
	[InlineData("site.zip", RemoteArchiveKind.Zip)]
	[InlineData(".hidden.tar.gz", RemoteArchiveKind.TarGzip)]
	[InlineData("notes.gz", RemoteArchiveKind.None)]
	[InlineData("site.tar.gz.part", RemoteArchiveKind.None)]
	[InlineData(".zip", RemoteArchiveKind.None)]
	[InlineData("zip", RemoteArchiveKind.None)]
	public void KindOf_GoesByHowTheNameEnds(string name, RemoteArchiveKind kind) =>
		Assert.Equal(kind, RemoteArchives.KindOf(name));

	[Theory]
	[InlineData("site.tar.gz", ".tar.gz")]
	[InlineData("Backup.TAR.XZ", ".TAR.XZ")]
	[InlineData("readme.txt", null)]
	public void ExtensionOf_KeepsTheSpellingOfTheName(string name, string? extension) =>
		Assert.Equal(extension, RemoteArchives.ExtensionOf(name));

	[Theory]
	[InlineData("site.tar.gz", "site")]
	[InlineData("v1.2.3.zip", "v1.2.3")]
	[InlineData("dump.tbz2", "dump")]
	public void FolderName_IsTheNameWithoutTheExtension(string name, string folder) =>
		Assert.Equal(folder, RemoteArchives.FolderName(name));

	[Fact]
	public void ArchiveName_ComesFromTheEntryOrItsFolder()
	{
		Assert.Equal("site.tar.gz", RemoteArchives.ArchiveName(["site"], "/srv"));
		Assert.Equal("www.tar.gz", RemoteArchives.ArchiveName(["a.html", "b.html"], "/var/www"));
		Assert.Equal("archive.tar.gz", RemoteArchives.ArchiveName(["etc", "srv"], "/"));
	}
}
