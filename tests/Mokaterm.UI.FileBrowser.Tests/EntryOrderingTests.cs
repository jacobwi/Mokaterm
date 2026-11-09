using Moka.Red.Data.FileBrowser;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.UI.FileBrowser.Browsing;

namespace Mokaterm.UI.FileBrowser.Tests;

// The rows on screen are sorted by MokaFileBrowser and the selection, type-ahead and delete work from the browser's own
// view, so the two orders have to agree row for row.
public sealed class EntryOrderingTests
{
	private static readonly DateTimeOffset Noon = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

	private static readonly RemoteFileEntry[] Entries =
	[
		File("log10.txt", 10, Noon),
		File("log2.txt", 10, Noon.AddHours(1)),
		File("alpha", 300, Noon.AddHours(-1)),
		File("Beta", 300, Noon),
		Folder("zeta", Noon.AddHours(2)),
		Folder("Alpha dir", Noon),
		new RemoteFileEntry { Path = "/srv/link", Name = "link", Kind = RemoteEntryKind.SymbolicLink, LinkTargetKind = RemoteEntryKind.Directory },
		new RemoteFileEntry { Path = "/srv/broken", Name = "broken", Kind = RemoteEntryKind.SymbolicLink },
	];

	public static TheoryData<string, bool, bool> Orders()
	{
		TheoryData<string, bool, bool> data = [];
		foreach (string column in new[] { EntryOrdering.NameColumn, EntryOrdering.SizeColumn, EntryOrdering.ModifiedColumn })
		{
			foreach (bool descending in new[] { false, true })
			{
				foreach (bool foldersFirst in new[] { false, true })
				{
					data.Add(column, descending, foldersFirst);
				}
			}
		}

		return data;
	}

	[Theory]
	[MemberData(nameof(Orders))]
	public void TheBrowsersView_MatchesTheRowsMokaDraws(string column, bool descending, bool foldersFirst)
	{
		List<RemoteFileEntry> drawn = [.. Entries];
		drawn.Sort((a, b) => AsMokaSorts(a, b, column, descending, foldersFirst));

		DirectoryView view = DirectoryView.Build(Entries, showHidden: true, filter: null, column, descending, foldersFirst);

		Assert.Equal(drawn.Select(entry => entry.Path), view.Entries.Select(entry => entry.Path));
	}

	[Fact]
	public void TheColumnIds_AreMokasForItsOwnSorts()
	{
		Assert.Equal(MokaFileSort.Name, EntryOrdering.NameColumn);
		Assert.Equal(MokaFileSort.Size, EntryOrdering.SizeColumn);
		Assert.Equal(MokaFileSort.Modified, EntryOrdering.ModifiedColumn);
	}

	// What MokaFileBrowser.CompareRows does with the Compare a column hands it.
	private static int AsMokaSorts(RemoteFileEntry a, RemoteFileEntry b, string column, bool descending, bool foldersFirst)
	{
		if (foldersFirst)
		{
			int group = (a.IsDirectoryLike ? 0 : 1).CompareTo(b.IsDirectoryLike ? 0 : 1);
			if (group != 0)
			{
				return group;
			}
		}

		int result = column == EntryOrdering.NameColumn
			? EntryOrdering.CompareNames(a.Name, b.Name)
			: EntryOrdering.Compare(a, b, column, descending: false, foldersFirst: false);
		if (result != 0)
		{
			return descending ? -result : result;
		}

		result = EntryOrdering.CompareNames(a.Name, b.Name);
		return result != 0 ? result : string.CompareOrdinal(a.Path, b.Path);
	}

	private static RemoteFileEntry File(string name, long size, DateTimeOffset modified) => new()
	{
		Path = "/srv/" + name,
		Name = name,
		Kind = RemoteEntryKind.File,
		Size = size,
		LastModified = modified,
	};

	private static RemoteFileEntry Folder(string name, DateTimeOffset modified) => new()
	{
		Path = "/srv/" + name,
		Name = name,
		Kind = RemoteEntryKind.Directory,
		LastModified = modified,
	};
}
