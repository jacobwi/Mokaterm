using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.Modules.Ssh.Shell;

/// <summary>
/// The characters a <see cref="RemoteSearchQuery"/> that ignores case counts as one: a glob compares
/// <see cref="char.ToUpperInvariant"/> of each UTF-16 unit, a piece of a name compares with
/// <see cref="StringComparison.OrdinalIgnoreCase"/>. Built from the runtime's own tables rather than a list of our own,
/// because which characters outside ASCII fold onto an ASCII letter (the long s onto S in a glob, today) is the
/// runtime's business and may change with it.
/// </summary>
internal static class NameCaseClasses
{
	private static readonly Lazy<Dictionary<char, char[]>> GlobGroups = new(() => Group(char.ToUpperInvariant));

	private static readonly Lazy<Dictionary<char, char[]>> PieceGroups = new(() => Group(OrdinalKey));

	/// <summary>Every character the query's comparison takes for <paramref name="c"/>, <paramref name="c"/> first.</summary>
	/// <param name="glob">The query is a glob (<see cref="RemoteSearchQuery.IsGlob"/>) rather than a piece of a name.</param>
	public static IReadOnlyList<char> Of(char c, bool glob)
	{
		Dictionary<char, char[]> groups = (glob ? GlobGroups : PieceGroups).Value;
		if (!groups.TryGetValue(glob ? char.ToUpperInvariant(c) : OrdinalKey(c), out char[]? members))
		{
			return [c];
		}

		// The comparison itself decides, so a key that grouped two characters wrongly cannot widen what matches.
		return [c, .. members.Where(member => member != c && (glob ? SameInGlob(member, c) : SameInPiece(member, c)))];
	}

	private static Dictionary<char, char[]> Group(Func<char, char> keyOf)
	{
		Dictionary<char, List<char>> groups = [];
		for (int code = 0; code <= char.MaxValue; code++)
		{
			char c = (char)code;
			if (char.IsSurrogate(c))
			{
				continue;
			}

			char key = keyOf(c);
			if (!groups.TryGetValue(key, out List<char>? members))
			{
				groups[key] = members = [];
			}

			members.Add(c);
		}

		return groups.Where(group => group.Value.Count > 1).ToDictionary(group => group.Key, group => group.Value.ToArray());
	}

	private static bool SameInGlob(char a, char b) => char.ToUpperInvariant(a) == char.ToUpperInvariant(b);

	private static bool SameInPiece(char a, char b) => string.Equals(a.ToString(), b.ToString(), StringComparison.OrdinalIgnoreCase);

	// OrdinalIgnoreCase folds like ToUpperInvariant but leaves some characters out (the long s among them), so a character
	// joins its upper case only where the comparison agrees.
	private static char OrdinalKey(char c)
	{
		char upper = char.ToUpperInvariant(c);
		return upper != c && SameInPiece(c, upper) ? upper : c;
	}
}
