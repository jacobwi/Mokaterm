using System.Globalization;

namespace Mokaterm.Plugins.Sample.Fail2ban;

/// <summary>
/// Reads what <see cref="Fail2banCommands.Status"/> printed: <c>fail2ban-client status</c>, then one part per jail that
/// starts with <see cref="Fail2banCommands.JailMarker"/>. Lines are the tree fail2ban draws (<c>|- Key:\tvalue</c>), so
/// a line's tree characters are dropped and the rest is split at its first colon; a line that is no such pair is
/// passed over.
/// </summary>
internal static class Fail2banParser
{
	private const string JailListKey = "Jail list";

	public static Fail2banStatus Parse(string output)
	{
		ArgumentNullException.ThrowIfNull(output);
		List<string> names = [];
		List<Fail2banJail> jails = [];
		string? jail = null;
		List<string> lines = [];
		foreach (string raw in output.ReplaceLineEndings("\n").Split('\n'))
		{
			if (raw.StartsWith(Fail2banCommands.JailMarker, StringComparison.Ordinal))
			{
				AddJail(jails, jail, lines);
				jail = raw[Fail2banCommands.JailMarker.Length..].Trim();
				lines = [];
			}
			else if (jail is null)
			{
				if (Pair(raw) is (JailListKey, { } list))
				{
					names.AddRange(list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
				}
			}
			else
			{
				lines.Add(raw);
			}
		}

		AddJail(jails, jail, lines);
		return new Fail2banStatus(names, jails);
	}

	private static void AddJail(List<Fail2banJail> jails, string? name, List<string> lines)
	{
		if (string.IsNullOrEmpty(name))
		{
			return;
		}

		Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
		foreach (string line in lines)
		{
			if (Pair(line) is ({ } key, { } value))
			{
				values[key] = value;
			}
		}

		bool answered = values.ContainsKey("Status for the jail");
		jails.Add(new Fail2banJail
		{
			Name = name,
			CurrentlyFailed = Count(values, "Currently failed"),
			TotalFailed = Count(values, "Total failed"),
			CurrentlyBanned = Count(values, "Currently banned"),
			TotalBanned = Count(values, "Total banned"),
			BannedAddresses = values.TryGetValue("Banned IP list", out string? banned)
				? banned.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
				: [],
			Source = values.TryGetValue("File list", out string? files) ? files
				: values.TryGetValue("Journal matches", out string? journal) ? journal
				: "",
			Problem = answered ? null : lines.Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0) ?? "fail2ban printed nothing for this jail.",
		});
	}

	/// <summary><c>|  |- Currently failed:\t1</c> as its key and value, or nulls for a line that holds no pair.</summary>
	private static (string? Key, string? Value) Pair(string line)
	{
		string text = line.TrimStart('|', '`', '-', ' ', '\t');
		int colon = text.IndexOf(':', StringComparison.Ordinal);
		return colon <= 0 ? (null, null) : (text[..colon].Trim(), text[(colon + 1)..].Trim());
	}

	private static int? Count(Dictionary<string, string> values, string key) =>
		values.TryGetValue(key, out string? value) && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int count) ? count : null;
}
