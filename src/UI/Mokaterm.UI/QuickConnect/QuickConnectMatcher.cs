using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.UI.QuickConnect;

/// <summary>
/// Picks the rows under the quick connect box. With nothing typed it offers what the picked protocol found on this
/// computer and then the history; with text it matches saved logins, the history and those names, best match first.
/// </summary>
internal static class QuickConnectMatcher
{
	/// <summary>A glance, not a browser: past this many rows typing one more letter is quicker than reading.</summary>
	public const int MaxRows = 8;

	private const string SchemeSeparator = "://";

	/// <param name="text">What the box holds. A <c>scheme://</c> keeps only rows of that protocol.</param>
	/// <param name="protocolId">The protocol the box would open; its rows come first among equally good matches.</param>
	public static IReadOnlyList<QuickConnectRow> Match(string? text, string protocolId, QuickConnectSources sources, int maxRows = MaxRows)
	{
		ArgumentNullException.ThrowIfNull(sources);
		string typed = text?.Trim() ?? "";
		string? scheme = QuickConnectTarget.ReadScheme(typed);
		string needle = scheme is null ? typed : typed[(typed.IndexOf(SchemeSeparator, StringComparison.Ordinal) + SchemeSeparator.Length)..].Trim();

		bool Wanted(string candidate) =>
			sources.Protocols.ContainsKey(candidate)
			&& (scheme is null || string.Equals(candidate, scheme, StringComparison.OrdinalIgnoreCase));

		List<Candidate> candidates = needle.Length == 0
			? Unfiltered(sources, Wanted)
			: Filtered(needle, sources, Wanted);

		if (needle.Length > 0)
		{
			candidates.Sort((left, right) => Compare(left, right, protocolId));
		}

		List<QuickConnectRow> rows = [];
		HashSet<string> seen = new(StringComparer.Ordinal);
		foreach (Candidate candidate in candidates)
		{
			// The same target twice is said once: a saved login over a recent entry, a recent entry over a found name.
			if (candidate.Row.TargetKey is { } key && !seen.Add(key))
			{
				continue;
			}

			rows.Add(candidate.Row);
			if (rows.Count == maxRows)
			{
				break;
			}
		}

		return rows;
	}

	/// <summary>
	/// How well <paramref name="field"/> matches <paramref name="needle"/>, without regard to case. A word starts after
	/// anything that is not a letter or a digit, so <c>db</c> finds <c>prod-db-2</c> and <c>10.0.0.4</c>'s last part.
	/// </summary>
	public static QuickConnectRank RankOf(string? field, string needle)
	{
		ArgumentNullException.ThrowIfNull(needle);
		if (string.IsNullOrEmpty(field) || needle.Length == 0)
		{
			return QuickConnectRank.None;
		}

		if (field.Equals(needle, StringComparison.OrdinalIgnoreCase))
		{
			return QuickConnectRank.Exact;
		}

		if (field.StartsWith(needle, StringComparison.OrdinalIgnoreCase))
		{
			return QuickConnectRank.Prefix;
		}

		int index = field.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
		if (index < 0)
		{
			return QuickConnectRank.None;
		}

		for (; index >= 0; index = field.IndexOf(needle, index + 1, StringComparison.OrdinalIgnoreCase))
		{
			if (!char.IsLetterOrDigit(field[index - 1]))
			{
				return QuickConnectRank.WordPrefix;
			}
		}

		return QuickConnectRank.Contains;
	}

	/// <summary>What the picked protocol found first, since picking it says what is wanted, then the history.</summary>
	private static List<Candidate> Unfiltered(QuickConnectSources sources, Func<string, bool> wanted)
	{
		List<Candidate> candidates = [];
		if (sources.DetectedProtocolId is { } detectedProtocol && wanted(detectedProtocol))
		{
			candidates.AddRange(sources.Detected.Select(name => new Candidate(QuickConnectRow.ForDetected(detectedProtocol, name, QuickConnectRank.None))));
		}

		Dictionary<string, HostProfile> hostsByAddress = HostsByAddress(sources.Catalog);
		candidates.AddRange(sources.Recents
			.Where(recent => wanted(recent.ProtocolId))
			.Select(recent => new Candidate(QuickConnectRow.ForRecent(recent, hostsByAddress.GetValueOrDefault(recent.Address), QuickConnectRank.None))));
		return candidates;
	}

	private static List<Candidate> Filtered(string needle, QuickConnectSources sources, Func<string, bool> wanted)
	{
		List<Candidate> candidates = [];
		Dictionary<Guid, HostProfile> hosts = [];
		foreach (HostProfile host in sources.Catalog.Hosts)
		{
			hosts.TryAdd(host.Id, host);
		}

		foreach (ConnectionProfile login in sources.Catalog.Connections)
		{
			if (!wanted(login.ProtocolId) || !hosts.TryGetValue(login.HostId, out HostProfile? host))
			{
				continue;
			}

			QuickConnectRank rank = RankLogin(needle, host, login);
			if (rank != QuickConnectRank.None)
			{
				candidates.Add(new Candidate(
					QuickConnectRow.ForLogin(host, login, sources.Protocols.GetValueOrDefault(login.ProtocolId), rank),
					login.IsFavorite,
					login.LastConnectedAt));
			}
		}

		Dictionary<string, HostProfile> hostsByAddress = HostsByAddress(sources.Catalog);
		foreach (QuickConnectRecent recent in sources.Recents)
		{
			if (!wanted(recent.ProtocolId))
			{
				continue;
			}

			QuickConnectRank rank = Best(
				RankOf(recent.Text, needle),
				RankOf(QuickConnectText.Format(recent.Username, recent.Address, null), needle),
				RankOf(recent.Address, needle),
				AtMost(QuickConnectRank.Prefix, RankOf(recent.Username, needle)));
			if (rank != QuickConnectRank.None)
			{
				candidates.Add(new Candidate(
					QuickConnectRow.ForRecent(recent, hostsByAddress.GetValueOrDefault(recent.Address), rank),
					LastUsed: recent.ConnectedAt));
			}
		}

		if (sources.DetectedProtocolId is { } detectedProtocol && wanted(detectedProtocol))
		{
			foreach (QuickConnectSuggestion name in sources.Detected)
			{
				QuickConnectRank rank = Best(
					RankOf(name.Address, needle),
					AtMost(QuickConnectRank.Prefix, RankOf(name.Description, needle)));
				if (rank != QuickConnectRank.None)
				{
					candidates.Add(new Candidate(QuickConnectRow.ForDetected(detectedProtocol, name, rank)));
				}
			}
		}

		return candidates;
	}

	/// <summary>
	/// A user name on its own is never exact (every login of <c>root</c> would be), and a tag is a word inside the
	/// login's description rather than its name, so neither outranks the name or the address.
	/// </summary>
	private static QuickConnectRank RankLogin(string needle, HostProfile host, ConnectionProfile login)
	{
		string endpoint = QuickConnectText.Format(login.Username, host.Address, null);
		QuickConnectRank rank = Best(
			RankOf(endpoint, needle),
			login.Port is { } port ? RankOf(QuickConnectText.Format(login.Username, host.Address, port), needle) : QuickConnectRank.None,
			RankOf(host.Address, needle),
			RankOf(host.Name, needle),
			RankOf(login.Label, needle),
			AtMost(QuickConnectRank.Prefix, RankOf(login.Username, needle)));

		foreach (string tag in host.Tags)
		{
			rank = Best(rank, AtMost(QuickConnectRank.WordPrefix, RankOf(tag, needle)));
		}

		return rank;
	}

	/// <summary>Rank first; then the box's own protocol; then saved before history before found names; then use.</summary>
	private static int Compare(Candidate left, Candidate right, string protocolId)
	{
		int order = left.Row.Rank.CompareTo(right.Row.Rank);
		if (order != 0)
		{
			return order;
		}

		order = IsPreferred(right, protocolId).CompareTo(IsPreferred(left, protocolId));
		if (order != 0)
		{
			return order;
		}

		order = left.Row.Kind.CompareTo(right.Row.Kind);
		if (order != 0)
		{
			return order;
		}

		order = right.Favorite.CompareTo(left.Favorite);
		if (order != 0)
		{
			return order;
		}

		order = Nullable.Compare(right.LastUsed, left.LastUsed);
		return order != 0
			? order
			: string.Compare(left.Row.Completion, right.Row.Completion, StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsPreferred(Candidate candidate, string protocolId) =>
		string.Equals(candidate.Row.ProtocolId, protocolId, StringComparison.OrdinalIgnoreCase);

	private static Dictionary<string, HostProfile> HostsByAddress(ConnectionCatalog catalog)
	{
		Dictionary<string, HostProfile> byAddress = new(StringComparer.OrdinalIgnoreCase);
		foreach (HostProfile host in catalog.Hosts)
		{
			byAddress.TryAdd(host.Address, host);
		}

		return byAddress;
	}

	private static QuickConnectRank Best(params ReadOnlySpan<QuickConnectRank> ranks)
	{
		QuickConnectRank best = QuickConnectRank.None;
		foreach (QuickConnectRank rank in ranks)
		{
			if (rank < best)
			{
				best = rank;
			}
		}

		return best;
	}

	/// <summary>No better than <paramref name="ceiling"/>, and still none when there was no match.</summary>
	private static QuickConnectRank AtMost(QuickConnectRank ceiling, QuickConnectRank rank) =>
		rank == QuickConnectRank.None || rank > ceiling ? rank : ceiling;

	private readonly record struct Candidate(QuickConnectRow Row, bool Favorite = false, DateTimeOffset? LastUsed = null);
}
