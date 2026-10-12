using System.Globalization;
using Mokaterm.Modules.Ssh.Shell;

namespace Mokaterm.Modules.Ssh.Admin.Security;

/// <summary>fail2ban on this machine: its jails, or why it said nothing about them.</summary>
internal sealed record Fail2BanStatus
{
	public IReadOnlyList<Fail2BanJail> Jails { get; init; } = [];

	/// <summary>Why the jails could not be read (fail2ban only answers root, or is not running), or null.</summary>
	public string? Problem { get; init; }

	/// <summary>
	/// <c>fail2ban-client status</c>, then <c>@@mt:jail NAME</c> and that jail's status for each one. Both print a tree
	/// whose lines are <c>|- Key:  value</c>.
	/// </summary>
	public static Fail2BanStatus Parse(string? overview, IEnumerable<(string Name, string Text)> jails)
	{
		ArgumentNullException.ThrowIfNull(jails);
		string text = overview ?? "";
		if (text.Contains("Permission denied", StringComparison.OrdinalIgnoreCase))
		{
			return new Fail2BanStatus { Problem = "fail2ban only answers root. Turn on root mode to read its jails." };
		}

		if (!text.Contains("Jail list", StringComparison.Ordinal))
		{
			string first = RemoteText.FirstLine(text);
			return new Fail2BanStatus { Problem = first.Length > 0 ? $"fail2ban did not answer: {first}" : "fail2ban did not answer. It may not be running." };
		}

		return new Fail2BanStatus { Jails = [.. jails.Select(jail => ParseJail(jail.Name, jail.Text))] };
	}

	private static Fail2BanJail ParseJail(string name, string text)
	{
		Dictionary<string, string> values = new(StringComparer.Ordinal);
		foreach (string line in AdminSections.Lines(text))
		{
			string trimmed = line.TrimStart(' ', '|', '`', '-', '\t');
			int colon = trimmed.IndexOf(':', StringComparison.Ordinal);
			if (colon > 0)
			{
				values.TryAdd(trimmed[..colon].Trim(), trimmed[(colon + 1)..].Trim());
			}
		}

		return new Fail2BanJail
		{
			Name = name,
			CurrentlyFailed = Number(values, "Currently failed"),
			TotalFailed = Number(values, "Total failed"),
			CurrentlyBanned = Number(values, "Currently banned"),
			TotalBanned = Number(values, "Total banned"),
			Banned = values.TryGetValue("Banned IP list", out string? banned) ? banned.Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries) : [],
			Watches = values.TryGetValue("File list", out string? files) ? files : values.TryGetValue("Journal matches", out string? matches) ? matches : "",
		};
	}

	private static int? Number(Dictionary<string, string> values, string key) =>
		values.TryGetValue(key, out string? value) && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int number) ? number : null;
}
