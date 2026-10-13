using System.Diagnostics.CodeAnalysis;

namespace Mokaterm.Abstractions.Plugins;

/// <summary>
/// How two plugin versions compare, for the install dialog's "updates" or "goes back" and the gallery's "update
/// available". A version is whatever the plugin's manifest says, so only a dotted number with an optional label after a
/// dash is put in order; anything else is unknown rather than older or newer.
/// </summary>
public static class PluginVersions
{
	/// <summary>
	/// Below zero when <paramref name="left"/> is older than <paramref name="right"/>, zero when they are the same version,
	/// above zero when it is newer, and null when either cannot be put in order.
	/// </summary>
	public static int? Compare(string? left, string? right)
	{
		if (!TryRead(left, out Version? leftNumber, out string? leftLabel) || !TryRead(right, out Version? rightNumber, out string? rightLabel))
		{
			return null;
		}

		int numbers = leftNumber.CompareTo(rightNumber);
		if (numbers != 0)
		{
			return Math.Sign(numbers);
		}

		// 1.0.0-beta comes before 1.0.0, as semantic versioning has it.
		return (leftLabel, rightLabel) switch
		{
			(null, null) => 0,
			(null, _) => 1,
			(_, null) => -1,
			_ => Math.Sign(string.CompareOrdinal(leftLabel, rightLabel)),
		};
	}

	/// <summary>True only when both versions can be put in order and <paramref name="candidate"/> comes later.</summary>
	public static bool IsNewer(string? candidate, string? than) => Compare(candidate, than) > 0;

	private static bool TryRead(string? text, [NotNullWhen(true)] out Version? number, out string? label)
	{
		number = null;
		label = null;
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}

		string value = text.Trim();
		int metadata = value.IndexOf('+', StringComparison.Ordinal);
		if (metadata >= 0)
		{
			value = value[..metadata];
		}

		int dash = value.IndexOf('-', StringComparison.Ordinal);
		if (dash >= 0)
		{
			label = value[(dash + 1)..];
			value = value[..dash];
			if (label.Length == 0)
			{
				return false;
			}
		}

		if (!value.Contains('.', StringComparison.Ordinal))
		{
			value += ".0";
		}

		if (!Version.TryParse(value, out Version? parsed))
		{
			return false;
		}

		// 1.2 and 1.2.0 are one version; Version alone would put 1.2 first.
		number = new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0), Math.Max(parsed.Revision, 0));
		return true;
	}
}
