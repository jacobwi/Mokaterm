using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.UI.FileBrowser.Browsing;

/// <summary>
/// Selected entries keyed by path, so a selection survives reloads. <see cref="Anchor"/> is where Shift ranges start;
/// <see cref="Focus"/> is the row the keyboard acts on.
/// </summary>
internal sealed class EntrySelection
{
	private readonly HashSet<string> _paths = new(StringComparer.Ordinal);

	public int Count => _paths.Count;

	public string? Anchor { get; private set; }

	public string? Focus { get; private set; }

	public bool Contains(string path) => _paths.Contains(path);

	public void SelectOnly(string path)
	{
		_paths.Clear();
		_paths.Add(path);
		Anchor = path;
		Focus = path;
	}

	public void Toggle(string path)
	{
		if (!_paths.Remove(path))
		{
			_paths.Add(path);
		}

		Anchor = path;
		Focus = path;
	}

	/// <summary>Replaces the selection with the rows between <see cref="Anchor"/> and <paramref name="path"/>.</summary>
	public void SelectRange(DirectoryView view, string path)
	{
		int end = view.IndexOf(path);
		if (end < 0)
		{
			return;
		}

		int start = view.IndexOf(Anchor);
		if (start < 0)
		{
			start = end;
			Anchor = path;
		}

		_paths.Clear();
		for (int index = Math.Min(start, end); index <= Math.Max(start, end); index++)
		{
			_paths.Add(view.Entries[index].Path);
		}

		Focus = path;
	}

	public void SelectAll(DirectoryView view)
	{
		foreach (RemoteFileEntry entry in view.Entries)
		{
			_paths.Add(entry.Path);
		}

		if (view.IndexOf(Focus) < 0 && view.Count > 0)
		{
			Focus = view.Entries[0].Path;
			Anchor = Focus;
		}
	}

	/// <summary>Clears the selection but keeps the keyboard position.</summary>
	public void Clear() => _paths.Clear();

	/// <summary>Forgets everything, for a different directory.</summary>
	public void Reset()
	{
		_paths.Clear();
		Anchor = null;
		Focus = null;
	}

	/// <summary>Drops paths that are no longer visible, after a reload, a filter change or hiding dot files.</summary>
	public void Retain(DirectoryView view)
	{
		_paths.RemoveWhere(path => view.IndexOf(path) < 0);
		if (view.IndexOf(Anchor) < 0)
		{
			Anchor = null;
		}

		if (view.IndexOf(Focus) < 0)
		{
			Focus = null;
		}
	}

	/// <summary>Selected entries in display order.</summary>
	public List<RemoteFileEntry> SelectedIn(DirectoryView view)
	{
		List<RemoteFileEntry> selected = new(_paths.Count);
		if (_paths.Count == 0)
		{
			return selected;
		}

		foreach (RemoteFileEntry entry in view.Entries)
		{
			if (_paths.Contains(entry.Path))
			{
				selected.Add(entry);
			}
		}

		return selected;
	}
}
