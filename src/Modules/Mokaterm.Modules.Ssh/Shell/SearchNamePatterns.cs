using System.Text;
using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.Modules.Ssh.Shell;

/// <summary>
/// The name patterns <see cref="RemoteScripts.Search"/> hands to find for a <see cref="RemoteSearchQuery"/>. They match
/// every name the query matches, and only those where a shell pattern can say exactly what the query means. The search
/// applies <see cref="RemoteSearchQuery.Matches"/> to everything that comes back whatever happened here, so a pattern
/// that matches more costs time on the server, never a result.
/// </summary>
/// <remarks>
/// find runs in the C locale, where a pattern compares bytes: literal text and <c>*</c> mean what the query means, and
/// ignoring case among ASCII letters is a bracket (<c>[aA]</c>). <c>?</c> is not (the query's means one UTF-16 unit,
/// which is one to three bytes, or half of four), and neither are a backslash (find's escape), a slash, a control
/// character, U+FFFD (what a name that is not UTF-8 reads as) or a lone surrogate: each of those becomes <c>*</c>. A
/// character whose other cases include one outside ASCII is written out as alternatives, one pattern for each choice,
/// up to <see cref="MaxAlternatives"/>; past that each such character becomes <c>*</c> too.
/// </remarks>
internal sealed record SearchNamePatterns
{
	/// <summary>The most patterns one search gives a find with <c>-printf</c>, each a <c>-name</c> of its own.</summary>
	public const int MaxAlternatives = 16;

	private const char ReplacementCharacter = (char)0xFFFD;

	private static readonly string[] Star = ["*"];

	/// <summary>Patterns for a find with <c>-printf</c>, any of which matches; empty matches every name.</summary>
	public required IReadOnlyList<string> Alternatives { get; init; }

	/// <summary><see cref="Alternatives"/> match the names the query matches and no others.</summary>
	public required bool AlternativesExact { get; init; }

	/// <summary>The one pattern for a find without <c>-printf</c>, whose names go through a shell <c>case</c>.</summary>
	public required string Single { get; init; }

	/// <summary><see cref="Single"/> matches the names the query matches and no others.</summary>
	public required bool SingleExact { get; init; }

	public static SearchNamePatterns For(RemoteSearchQuery query)
	{
		ArgumentNullException.ThrowIfNull(query);
		if (query.Pattern.Length == 0)
		{
			return new SearchNamePatterns { Alternatives = [], AlternativesExact = true, Single = "*", SingleExact = true };
		}

		bool glob = query.IsGlob;
		bool exact = true;
		List<string[]> atoms = [];
		if (!glob)
		{
			atoms.Add(Star);
		}

		string pattern = query.Pattern;
		for (int index = 0; index < pattern.Length; index++)
		{
			char c = pattern[index];
			if (glob && c == '*')
			{
				atoms.Add(Star);
				continue;
			}

			if (char.IsHighSurrogate(c) && index + 1 < pattern.Length && char.IsLowSurrogate(pattern[index + 1]))
			{
				// A glob compares UTF-16 units, where a surrogate has no other case; a piece of a name folds whole
				// characters, the ones outside the Basic Multilingual Plane included.
				bool literal = query.MatchCase || glob;
				atoms.Add(literal ? [pattern.Substring(index, 2)] : Star);
				exact &= literal;
				index++;
				continue;
			}

			string[]? atom = glob && c == '?' ? null : Spell(c, query.MatchCase, glob);
			atoms.Add(atom ?? Star);
			exact &= atom is not null;
		}

		if (!glob)
		{
			atoms.Add(Star);
		}

		long combinations = 1;
		foreach (string[] atom in atoms)
		{
			combinations = Math.Min(combinations * atom.Length, MaxAlternatives + 1L);
		}

		string widened = Join(atoms.Select(atom => atom.Length == 1 ? atom[0] : "*"));
		bool widenedExact = exact && combinations == 1;
		if (combinations > MaxAlternatives)
		{
			return new SearchNamePatterns { Alternatives = [widened], AlternativesExact = false, Single = widened, SingleExact = false };
		}

		List<string> alternatives = [.. Expand(atoms)];
		return new SearchNamePatterns
		{
			Alternatives = alternatives,
			AlternativesExact = exact,
			Single = widened,
			SingleExact = widenedExact,
		};
	}

	/// <summary>The ways a find pattern can write <paramref name="c"/>, or null when none says exactly that character.</summary>
	private static string[]? Spell(char c, bool matchCase, bool glob)
	{
		if (!IsSpellable(c))
		{
			return null;
		}

		if (c == '[')
		{
			return ["[[]"];
		}

		if (matchCase)
		{
			return [c.ToString()];
		}

		IReadOnlyList<char> same = NameCaseClasses.Of(c, glob);
		if (same.Any(other => !IsSpellable(other) || other == '['))
		{
			return null;
		}

		string ascii = new([.. same.Where(char.IsAscii)]);
		List<string> spellings = [];
		if (ascii.Length > 0)
		{
			spellings.Add(ascii.Length == 1 ? ascii : "[" + ascii + "]");
		}

		spellings.AddRange(same.Where(other => !char.IsAscii(other)).Select(other => other.ToString()));
		return [.. spellings];
	}

	private static bool IsSpellable(char c) =>
		!char.IsControl(c) && !char.IsSurrogate(c) && c is not ('*' or '?' or '\\' or '/' or ReplacementCharacter);

	private static IEnumerable<string> Expand(List<string[]> atoms)
	{
		IEnumerable<string> patterns = [""];
		foreach (string[] atom in atoms)
		{
			patterns = [.. patterns.SelectMany(prefix => atom.Select(spelling => prefix + spelling))];
		}

		return patterns.Select(Collapse);
	}

	private static string Join(IEnumerable<string> atoms) => Collapse(string.Concat(atoms));

	// No atom holds a literal star (a star is the glob's, or what a character became), so a run of them is one.
	private static string Collapse(string pattern)
	{
		StringBuilder builder = new(pattern.Length);
		foreach (char c in pattern)
		{
			if (c != '*' || builder.Length == 0 || builder[^1] != '*')
			{
				builder.Append(c);
			}
		}

		return builder.ToString();
	}
}
