using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.UI.FileBrowser.Browsing;

/// <summary>The visible rows of a listing: hidden entries and the name filter applied, sorted, indexed by path.</summary>
internal sealed class DirectoryView
{
	private readonly Dictionary<string, int> _indexByPath;

	private DirectoryView(List<RemoteFileEntry> entries)
	{
		Entries = entries;
		_indexByPath = new Dictionary<string, int>(entries.Count, StringComparer.Ordinal);
		for (int index = 0; index < entries.Count; index++)
		{
			_indexByPath[entries[index].Path] = index;
		}
	}

	public static DirectoryView Empty { get; } = new([]);

	/// <summary>Rows in display order. Every build returns a new list because the table reloads on a new reference only.</summary>
	public IReadOnlyList<RemoteFileEntry> Entries { get; }

	public int Count => Entries.Count;

	public int IndexOf(string? path) => path is not null && _indexByPath.TryGetValue(path, out int index) ? index : -1;

	public RemoteFileEntry? Find(string? path) => IndexOf(path) is >= 0 and var index ? Entries[index] : null;

	/// <param name="sortColumn">Column to sort by; null keeps the natural order (by name, ascending).</param>
	public static DirectoryView Build(
		IReadOnlyList<RemoteFileEntry> entries,
		bool showHidden,
		string? filter,
		string? sortColumn,
		bool descending,
		bool foldersFirst)
	{
		string trimmedFilter = filter?.Trim() ?? "";
		HashSet<string> seen = new(StringComparer.Ordinal);
		List<RemoteFileEntry> rows = new(entries.Count);
		foreach (RemoteFileEntry entry in entries)
		{
			if ((showHidden || !entry.IsHidden)
				&& (trimmedFilter.Length == 0 || entry.Name.Contains(trimmedFilter, StringComparison.OrdinalIgnoreCase))
				// The table throws on duplicate row keys, and some FTP servers list an entry twice.
				&& seen.Add(entry.Path))
			{
				rows.Add(entry);
			}
		}

		string column = sortColumn ?? EntryOrdering.NameColumn;
		bool reverse = sortColumn is not null && descending;
		rows.Sort((a, b) => reverse
			? EntryOrdering.Compare(b, a, column, descending: true, foldersFirst)
			: EntryOrdering.Compare(a, b, column, descending: false, foldersFirst));
		return new DirectoryView(rows);
	}
}
