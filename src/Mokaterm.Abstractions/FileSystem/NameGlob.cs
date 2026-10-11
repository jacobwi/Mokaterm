namespace Mokaterm.Abstractions.FileSystem;

/// <summary>
/// Shell-style matching of one name: <c>*</c> stands for any run of characters, <c>?</c> for one, and everything else for
/// itself. There is no escape character, because a backslash is an ordinary character in a POSIX name.
/// </summary>
internal static class NameGlob
{
	public static bool IsMatch(ReadOnlySpan<char> pattern, ReadOnlySpan<char> name, bool ignoreCase)
	{
		int p = 0;
		int n = 0;

		// The last star seen, and where in the name it started matching. A mismatch lets that star take one more character.
		// Earlier stars never need to: whatever they would take, the last one can take instead.
		int star = -1;
		int starFrom = 0;
		while (n < name.Length)
		{
			if (p < pattern.Length && pattern[p] == '*')
			{
				star = p++;
				starFrom = n;
			}
			else if (p < pattern.Length && (pattern[p] == '?' || Same(pattern[p], name[n], ignoreCase)))
			{
				p++;
				n++;
			}
			else if (star >= 0)
			{
				p = star + 1;
				n = ++starFrom;
			}
			else
			{
				return false;
			}
		}

		while (p < pattern.Length && pattern[p] == '*')
		{
			p++;
		}

		return p == pattern.Length;
	}

	private static bool Same(char a, char b, bool ignoreCase) =>
		a == b || (ignoreCase && char.ToUpperInvariant(a) == char.ToUpperInvariant(b));
}
