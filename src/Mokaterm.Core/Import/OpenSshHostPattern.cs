namespace Mokaterm.Core.Import;

/// <summary>The <c>Host</c> pattern language: <c>*</c>, <c>?</c> and a leading <c>!</c> to exclude.</summary>
internal static class OpenSshHostPattern
{
	/// <summary>True when the pattern stands for a set of hosts instead of naming one.</summary>
	public static bool IsWildcard(string pattern) =>
		pattern.Length == 0
		|| pattern[0] == '!'
		|| pattern.Contains('*', StringComparison.Ordinal)
		|| pattern.Contains('?', StringComparison.Ordinal);

	/// <summary>
	/// True when <paramref name="patterns"/> covers <paramref name="host"/>: one plain pattern matches and no
	/// negated pattern does, which is how OpenSSH reads a <c>Host</c> line.
	/// </summary>
	public static bool Covers(IReadOnlyList<string> patterns, string host)
	{
		long budget = long.MaxValue;
		return Covers(patterns, host, ref budget);
	}

	/// <summary>
	/// <see cref="Covers(IReadOnlyList{string}, string)"/> that takes one unit of <paramref name="budget"/> for every
	/// pattern and every character comparison. It gives up and returns false once the budget is spent, so a caller that
	/// sees a budget of zero or less cannot trust the answer.
	/// </summary>
	public static bool Covers(IReadOnlyList<string> patterns, string host, ref long budget)
	{
		bool matched = false;
		for (int index = 0; index < patterns.Count; index++)
		{
			if (--budget <= 0)
			{
				return false;
			}

			string pattern = patterns[index];
			if (pattern.StartsWith('!'))
			{
				if (Matches(pattern.AsSpan(1), host, ref budget))
				{
					return false;
				}
			}
			else if (Matches(pattern, host, ref budget))
			{
				matched = true;
			}

			if (budget <= 0)
			{
				return false;
			}
		}

		return matched;
	}

	public static bool Matches(string pattern, string value)
	{
		long budget = long.MaxValue;
		return Matches(pattern, value, ref budget);
	}

	/// <summary>
	/// <paramref name="value"/> with every character lowercased the way the matcher compares them, so two names fold to
	/// the same string exactly when a pattern of one without wildcards matches the other.
	/// </summary>
	public static string Fold(string value) =>
		string.Create(value.Length, value, static (folded, source) =>
		{
			for (int index = 0; index < source.Length; index++)
			{
				folded[index] = char.ToLowerInvariant(source[index]);
			}
		});

	private static bool Matches(ReadOnlySpan<char> pattern, ReadOnlySpan<char> value, ref long budget)
	{
		int patternIndex = 0;
		int valueIndex = 0;
		int starIndex = -1;
		int starValueIndex = 0;
		while (valueIndex < value.Length)
		{
			// A star backtracks, so a crafted pattern costs its length times the host's; the budget bounds that.
			if (--budget <= 0)
			{
				return false;
			}

			if (patternIndex < pattern.Length && (pattern[patternIndex] == '?' || Same(pattern[patternIndex], value[valueIndex])))
			{
				patternIndex++;
				valueIndex++;
			}
			else if (patternIndex < pattern.Length && pattern[patternIndex] == '*')
			{
				starIndex = patternIndex++;
				starValueIndex = valueIndex;
			}
			else if (starIndex >= 0)
			{
				patternIndex = starIndex + 1;
				valueIndex = ++starValueIndex;
			}
			else
			{
				return false;
			}
		}

		while (patternIndex < pattern.Length && pattern[patternIndex] == '*')
		{
			patternIndex++;
		}

		return patternIndex == pattern.Length;
	}

	// OpenSSH lowercases both sides before comparing.
	private static bool Same(char left, char right) => char.ToLowerInvariant(left) == char.ToLowerInvariant(right);
}
