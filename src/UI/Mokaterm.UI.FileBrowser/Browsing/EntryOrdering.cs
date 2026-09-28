using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.UI.FileBrowser.Browsing;

/// <summary>
/// Sort order of a listing. The table sorts its rows with these comparisons as well, so the browser's own view and
/// the rendered rows agree on every row index.
/// </summary>
internal static class EntryOrdering
{
	public const string NameColumn = "Name";

	public const string SizeColumn = "Size";

	public const string ModifiedColumn = "Modified";

	public const string PermissionsColumn = "Permissions";

	public const string OwnerColumn = "Owner";

	/// <summary>
	/// Compares for a sort by <paramref name="column"/>. A descending sort reverses the whole result, so with
	/// <paramref name="foldersFirst"/> the folder group is inverted up front to keep folders on top either way.
	/// Ties fall back to the name and then the path, which makes the order total.
	/// </summary>
	public static int Compare(RemoteFileEntry a, RemoteFileEntry b, string column, bool descending, bool foldersFirst)
	{
		if (foldersFirst)
		{
			int group = GroupOf(a).CompareTo(GroupOf(b));
			if (group != 0)
			{
				return descending ? -group : group;
			}
		}

		int result = column switch
		{
			SizeColumn => SizeOf(a).CompareTo(SizeOf(b)),
			ModifiedColumn => Nullable.Compare(a.LastModified, b.LastModified),
			PermissionsColumn => Nullable.Compare(a.Permissions, b.Permissions),
			OwnerColumn => string.Compare(OwnerLabel(a), OwnerLabel(b), StringComparison.OrdinalIgnoreCase),
			_ => 0,
		};

		if (result != 0)
		{
			return result;
		}

		result = CompareNames(a.Name, b.Name);
		return result != 0 ? result : string.CompareOrdinal(a.Path, b.Path);
	}

	/// <summary>
	/// Case-insensitive, with runs of digits compared by value so <c>log2</c> sorts before <c>log10</c>. Leading zeros
	/// (<c>log02</c>) only decide between names that are otherwise equal. Text runs compare ordinally: culture rules
	/// applied to fragments of a name can make the order intransitive.
	/// </summary>
	public static int CompareNames(string a, string b)
	{
		int i = 0;
		int j = 0;
		int zeros = 0;
		while (i < a.Length && j < b.Length)
		{
			int startA = i;
			int startB = j;
			int result;
			if (char.IsAsciiDigit(a[i]) && char.IsAsciiDigit(b[j]))
			{
				i = SkipRun(a, i, digits: true);
				j = SkipRun(b, j, digits: true);
				ReadOnlySpan<char> digitsA = a.AsSpan(startA, i - startA);
				ReadOnlySpan<char> digitsB = b.AsSpan(startB, j - startB);
				result = CompareNumbers(digitsA.TrimStart('0'), digitsB.TrimStart('0'));
				if (result == 0 && zeros == 0)
				{
					zeros = digitsA.Length.CompareTo(digitsB.Length);
				}
			}
			else
			{
				i = SkipRun(a, i, digits: false);
				j = SkipRun(b, j, digits: false);
				result = a.AsSpan(startA, i - startA).CompareTo(b.AsSpan(startB, j - startB), StringComparison.OrdinalIgnoreCase);
			}

			if (result != 0)
			{
				return result;
			}
		}

		int length = (a.Length - i).CompareTo(b.Length - j);
		return length != 0 ? length : zeros;
	}

	/// <summary><c>owner:group</c>, just the owner, or empty when the server reports neither.</summary>
	public static string OwnerLabel(RemoteFileEntry entry) => (entry.Owner, entry.Group) switch
	{
		(null, null) => "",
		(null, { } group) => ":" + group,
		({ } owner, null) => owner,
		({ } owner, { } group) => owner + ":" + group,
	};

	private static int GroupOf(RemoteFileEntry entry) => entry.IsDirectoryLike ? 0 : 1;

	private static long SizeOf(RemoteFileEntry entry) => entry.Kind == RemoteEntryKind.File ? entry.Size : -1;

	private static int SkipRun(string text, int index, bool digits)
	{
		while (index < text.Length && char.IsAsciiDigit(text[index]) == digits)
		{
			index++;
		}

		return index;
	}

	// Both runs have their leading zeros removed, so a longer run is a larger number.
	private static int CompareNumbers(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
	{
		int result = a.Length.CompareTo(b.Length);
		return result != 0 ? result : a.SequenceCompareTo(b);
	}
}
