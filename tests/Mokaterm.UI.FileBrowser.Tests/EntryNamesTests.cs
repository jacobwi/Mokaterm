using Mokaterm.UI.FileBrowser.Browsing;

namespace Mokaterm.UI.FileBrowser.Tests;

public sealed class EntryNamesTests
{
	[Theory]
	[InlineData("report.txt", false, "report", ".txt")]
	[InlineData("site.tar.gz", false, "site", ".tar.gz")]
	[InlineData("backup.TGZ", false, "backup", ".TGZ")]
	[InlineData(".bashrc", false, ".bashrc", "")]
	[InlineData(".env.local", false, ".env", ".local")]
	[InlineData("Makefile", false, "Makefile", "")]
	[InlineData("trailing.", false, "trailing.", "")]
	[InlineData("v1.2", true, "v1.2", "")]
	public void Split_KeepsTheExtensionAFileOpensWith(string name, bool isFolder, string stem, string extension) =>
		Assert.Equal((stem, extension), EntryNames.Split(name, isFolder));

	[Fact]
	public void Unique_NumbersBeforeTheExtension()
	{
		HashSet<string> taken = new(StringComparer.Ordinal) { "untitled.txt", "untitled 2.txt" };

		Assert.Equal("untitled 3.txt", EntryNames.Unique("untitled.txt", taken, isFolder: false));
		Assert.Equal("fresh.txt", EntryNames.Unique("fresh.txt", taken, isFolder: false));
	}

	[Fact]
	public void Unique_NumbersAFolderAtTheEnd()
	{
		HashSet<string> taken = new(StringComparer.Ordinal) { "New folder", "site.d" };

		Assert.Equal("New folder 2", EntryNames.Unique("New folder", taken, isFolder: true));
		Assert.Equal("site.d 2", EntryNames.Unique("site.d", taken, isFolder: true));
	}

	[Theory]
	[InlineData("report.txt", false, "report copy.txt")]
	[InlineData("site.tar.gz", false, "site copy.tar.gz")]
	[InlineData(".bashrc", false, ".bashrc copy")]
	[InlineData("conf.d", true, "conf.d copy")]
	[InlineData("report copy.txt", false, "report copy 2.txt")]
	[InlineData("report copy 7.txt", false, "report copy.txt")]
	public void CopyName_AddsCopyBeforeTheExtension(string name, bool isFolder, string expected)
	{
		HashSet<string> taken = new(StringComparer.Ordinal) { name };

		Assert.Equal(expected, EntryNames.CopyName(name, taken, isFolder));
	}

	[Fact]
	public void CopyName_CountsOnPastCopiesAlreadyThere()
	{
		HashSet<string> taken = new(StringComparer.Ordinal) { "a.txt", "a copy.txt", "a copy 2.txt" };

		Assert.Equal("a copy 3.txt", EntryNames.CopyName("a.txt", taken, isFolder: false));
	}

	[Fact]
	public void CopyName_OfAFileCalledCopy_IsNotMistakenForACopy()
	{
		HashSet<string> taken = new(StringComparer.Ordinal) { "copy.txt" };

		Assert.Equal("copy copy.txt", EntryNames.CopyName("copy.txt", taken, isFolder: false));
	}
}
