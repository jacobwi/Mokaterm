namespace Mokaterm.DevHost.Demo;

/// <summary>
/// Deterministic pseudo-random numbers, so generated sizes, dates, log lines and file bytes look varied yet come out the
/// same on every run.
/// </summary>
internal static class DemoRandom
{
	/// <summary>A value in [0, <paramref name="max"/>) derived from <paramref name="seed"/>.</summary>
	public static int Next(ulong seed, int max) => (int)(Mix(seed) % (ulong)max);

	/// <summary>A seed for a string that stays the same across processes, unlike <see cref="string.GetHashCode()"/>.</summary>
	public static ulong Seed(string text)
	{
		// FNV-1a over the UTF-16 code units.
		ulong hash = 14695981039346656037;
		foreach (char c in text)
		{
			hash = (hash ^ c) * 1099511628211;
		}

		return hash;
	}

	/// <summary>The SplitMix64 finalizer: every input bit affects every output bit.</summary>
	public static ulong Mix(ulong value)
	{
		value += 0x9E3779B97F4A7C15;
		value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9;
		value = (value ^ (value >> 27)) * 0x94D049BB133111EB;
		return value ^ (value >> 31);
	}
}
