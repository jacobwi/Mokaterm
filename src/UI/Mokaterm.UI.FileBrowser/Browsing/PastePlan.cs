using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.UI.FileBrowser.Browsing;

/// <summary>Where each copy of a paste goes, from the names the target folder already holds.</summary>
internal static class PastePlan
{
	/// <summary>
	/// The names of <paramref name="sources"/> that <paramref name="existing"/> already holds in
	/// <paramref name="targetDirectory"/>, which is what a paste asks about. An entry pasted into its own folder is left out:
	/// replacing it with itself means nothing, so it always becomes a copy.
	/// </summary>
	public static List<string> Conflicts(IReadOnlyList<RemoteFileEntry> sources, string targetDirectory, IReadOnlySet<string> existing)
	{
		ArgumentNullException.ThrowIfNull(sources);
		ArgumentNullException.ThrowIfNull(existing);
		return [.. sources.Where(source => existing.Contains(source.Name) && !IsIn(source, targetDirectory)).Select(source => source.Name)];
	}

	/// <summary>
	/// One step per source, in order. A free name is kept; a taken one is replaced under
	/// <see cref="PasteConflictChoice.Overwrite"/>, and otherwise, or when the entry is the one already there, the copy takes
	/// a name nothing has yet, counting the copies made earlier in the same paste.
	/// </summary>
	public static List<PasteStep> Build(
		IReadOnlyList<RemoteFileEntry> sources,
		string targetDirectory,
		IReadOnlySet<string> existing,
		PasteConflictChoice choice)
	{
		ArgumentNullException.ThrowIfNull(sources);
		ArgumentNullException.ThrowIfNull(existing);
		HashSet<string> taken = new(existing, StringComparer.Ordinal);
		List<PasteStep> steps = [];
		foreach (RemoteFileEntry source in sources)
		{
			string name = source.Name;
			bool overwrite = false;
			if (taken.Contains(name))
			{
				if (choice == PasteConflictChoice.Overwrite && !IsIn(source, targetDirectory))
				{
					overwrite = true;
				}
				else
				{
					name = EntryNames.CopyName(name, taken, source.IsDirectoryLike);
				}
			}

			taken.Add(name);
			steps.Add(new PasteStep(source, RemotePath.Combine(targetDirectory, name), overwrite));
		}

		return steps;
	}

	/// <summary>True when <paramref name="entry"/> already lives directly in <paramref name="directory"/>.</summary>
	public static bool IsIn(RemoteFileEntry entry, string directory) =>
		string.Equals(RemotePath.GetParent(entry.Path), RemotePath.Normalize(directory), StringComparison.Ordinal);

	/// <summary>True when pasting <paramref name="entry"/> into <paramref name="directory"/> would put a folder inside itself.</summary>
	public static bool IsIntoItself(RemoteFileEntry entry, string directory) =>
		entry.Kind == RemoteEntryKind.Directory && RemotePath.IsSameOrInside(directory, entry.Path);
}
