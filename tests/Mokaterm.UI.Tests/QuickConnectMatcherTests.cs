using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.UI.QuickConnect;
using Mokaterm.UI.Tests.Fakes;

namespace Mokaterm.UI.Tests;

/// <summary>
/// What the quick connect list offers and in what order: the best match first whatever kind of row it is, the picked
/// protocol ahead of the rest among equals, one row per target, and never more than a glance's worth.
/// </summary>
public sealed class QuickConnectMatcherTests
{
	private static readonly HostProfile Web = new() { Id = Guid.NewGuid(), Name = "web-prod", Address = "10.0.0.4", Tags = ["frontend"] };

	private static readonly HostProfile Db = new() { Id = Guid.NewGuid(), Name = "prod-db-2", Address = "db2.example.com" };

	private static readonly HostProfile Web2 = new() { Id = Guid.NewGuid(), Address = "web2.internal" };

	private static readonly ConnectionProfile WebRoot = Login(Web, "ssh", "root");

	private static readonly ConnectionProfile WebDeploy = Login(Web, "rdp", "deploy");

	private static readonly ConnectionProfile DbAdmin = Login(Db, "ssh", "admin") with { Label = "Billing database" };

	private static readonly ConnectionProfile Web2Ops = Login(Web2, "ssh", "ops");

	private static readonly ConnectionCatalog Catalog = new([], [Web, Db, Web2], [WebRoot, WebDeploy, DbAdmin, Web2Ops]);

	[Fact]
	public void Match_ExactBeforePrefixBeforeWordBeforeAnywhere()
	{
		HostProfile exact = new() { Id = Guid.NewGuid(), Address = "web" };
		HostProfile inside = new() { Id = Guid.NewGuid(), Address = "cobweb.example" };
		ConnectionCatalog catalog = new([], [inside, Db, Web2, exact], [Login(inside, "ssh", null), Login(Db, "ssh", null) with { Label = "old web box" }, Login(Web2, "ssh", null), Login(exact, "ssh", null)]);

		IReadOnlyList<QuickConnectRow> rows = QuickConnectMatcher.Match("web", "ssh", Sources(catalog));

		Assert.Equal(["web", "web2.internal", "db2.example.com", "cobweb.example"], rows.Select(row => row.Host!.Address));
		Assert.Equal(
			[QuickConnectRank.Exact, QuickConnectRank.Prefix, QuickConnectRank.WordPrefix, QuickConnectRank.Contains],
			rows.Select(row => row.Rank));
	}

	[Theory]
	[InlineData("root@10", "10.0.0.4")]
	[InlineData("billing", "db2.example.com")]
	[InlineData("db", "db2.example.com")]
	[InlineData("frontend", "10.0.0.4")]
	[InlineData("10.0.0.4", "10.0.0.4")]
	public void Match_FindsLoginsByNameAddressUserAndTag(string text, string address) =>
		Assert.Equal(address, QuickConnectMatcher.Match(text, "ssh", Sources(Catalog))[0].Host!.Address);

	// Every root login starts with root@, so the user name alone must not make one of them look exact.
	[Fact]
	public void Match_UserNameAlone_IsNeverExact()
	{
		QuickConnectRow row = Assert.Single(QuickConnectMatcher.Match("root", "ssh", Sources(Catalog)));

		Assert.Equal(QuickConnectRank.Prefix, row.Rank);
	}

	[Fact]
	public void Match_AmongEqualMatches_ThePickedProtocolComesFirst()
	{
		IReadOnlyList<QuickConnectRow> rdpFirst = QuickConnectMatcher.Match("web-prod", "rdp", Sources(Catalog));
		IReadOnlyList<QuickConnectRow> sshFirst = QuickConnectMatcher.Match("web-prod", "ssh", Sources(Catalog));

		Assert.Equal(["rdp", "ssh"], rdpFirst.Select(row => row.ProtocolId));
		Assert.Equal(["ssh", "rdp"], sshFirst.Select(row => row.ProtocolId));
	}

	[Fact]
	public void Match_AScheme_KeepsOnlyThatProtocol()
	{
		IReadOnlyList<QuickConnectRow> rows = QuickConnectMatcher.Match("rdp://web", "ssh", Sources(Catalog));

		QuickConnectRow row = Assert.Single(rows);
		Assert.Equal(WebDeploy.Id, row.Connection!.Id);
	}

	[Fact]
	public void Match_FavoritesAndRecentUse_BreakTies()
	{
		HostProfile a = new() { Id = Guid.NewGuid(), Address = "app-a" };
		HostProfile b = new() { Id = Guid.NewGuid(), Address = "app-b" };
		HostProfile c = new() { Id = Guid.NewGuid(), Address = "app-c" };
		ConnectionCatalog catalog = new(
			[],
			[a, b, c],
			[
				Login(a, "ssh", null),
				Login(b, "ssh", null) with { LastConnectedAt = DateTimeOffset.UnixEpoch.AddDays(2) },
				Login(c, "ssh", null) with { IsFavorite = true },
			]);

		IReadOnlyList<QuickConnectRow> rows = QuickConnectMatcher.Match("app", "ssh", Sources(catalog));

		Assert.Equal(["app-c", "app-b", "app-a"], rows.Select(row => row.Host!.Address));
	}

	[Fact]
	public void Match_RecentEntries_MatchByTarget()
	{
		IReadOnlyList<QuickConnectRow> rows = QuickConnectMatcher.Match(
			"pi@",
			"ssh",
			Sources(Catalog, [Recent("ssh", "pi", "raspberrypi.local", 2222), Recent("vnc", null, "10.0.0.9", null)]));

		QuickConnectRow row = Assert.Single(rows);
		Assert.Equal(QuickConnectRowKind.Recent, row.Kind);
		Assert.Equal("pi@raspberrypi.local:2222", row.Completion);
	}

	// One target is said once, by the row that says the most about it.
	[Fact]
	public void Match_ARecentEntryForASavedLogin_IsLeftOut()
	{
		IReadOnlyList<QuickConnectRow> rows = QuickConnectMatcher.Match(
			"10.0.0",
			"ssh",
			Sources(Catalog, [Recent("ssh", "root", "10.0.0.4", null), Recent("ssh", "root", "10.0.0.7", null)]));

		Assert.Equal(
			[(QuickConnectRowKind.SavedLogin, "root@10.0.0.4"), (QuickConnectRowKind.Recent, "root@10.0.0.7"), (QuickConnectRowKind.SavedLogin, "deploy@10.0.0.4")],
			rows.Select(row => (row.Kind, row.Completion)));
	}

	[Fact]
	public void Match_NothingTyped_OffersFoundNamesThenTheHistory()
	{
		QuickConnectSources sources = Sources(
			Catalog,
			[Recent("ssh", "root", "a.example", null), Recent("local", null, "pwsh", null), Recent("vnc", null, "b.example", 5901)],
			"local",
			[new QuickConnectSuggestion("pwsh", "PowerShell"), new QuickConnectSuggestion("Ubuntu", "WSL distribution")]);

		IReadOnlyList<QuickConnectRow> rows = QuickConnectMatcher.Match("", "local", sources);

		Assert.Equal(
			[(QuickConnectRowKind.Detected, "pwsh"), (QuickConnectRowKind.Detected, "Ubuntu"), (QuickConnectRowKind.Recent, "root@a.example"), (QuickConnectRowKind.Recent, "b.example:5901")],
			rows.Select(row => (row.Kind, row.Completion)));
	}

	[Fact]
	public void Match_FoundNames_AreMatchedByNameAndDescription()
	{
		QuickConnectSources sources = Sources(
			ConnectionCatalog.Empty,
			[],
			"local",
			[new QuickConnectSuggestion("pwsh", "PowerShell"), new QuickConnectSuggestion("cmd", "Command Prompt"), new QuickConnectSuggestion("Ubuntu", "WSL distribution")]);

		Assert.Equal(["pwsh"], QuickConnectMatcher.Match("power", "local", sources).Select(row => row.Completion));
		Assert.Equal(["Ubuntu"], QuickConnectMatcher.Match("wsl", "local", sources).Select(row => row.Completion));
		Assert.Equal(["cmd"], QuickConnectMatcher.Match("CMD", "local", sources).Select(row => row.Completion));
	}

	[Fact]
	public void Match_StopsAtTheCap()
	{
		List<HostProfile> hosts = [.. Enumerable.Range(0, 20).Select(index => new HostProfile { Id = Guid.NewGuid(), Address = $"node-{index:00}" })];
		ConnectionCatalog catalog = new([], hosts, [.. hosts.Select(host => Login(host, "ssh", null))]);

		Assert.Equal(QuickConnectMatcher.MaxRows, QuickConnectMatcher.Match("node", "ssh", Sources(catalog)).Count);
		Assert.Equal(3, QuickConnectMatcher.Match("node", "ssh", Sources(catalog), maxRows: 3).Count);
	}

	// A login over a module this host does not load (serial on the web host) cannot be opened, so it is not offered.
	[Fact]
	public void Match_LeavesOutProtocolsThisHostCannotOpen()
	{
		HostProfile board = new() { Id = Guid.NewGuid(), Address = "COM7" };
		ConnectionCatalog catalog = new([], [board], [Login(board, "serial", null)]);
		QuickConnectSources sources = new()
		{
			Catalog = catalog,
			Protocols = Descriptors(QuickConnectProtocols.Ssh),
		};

		Assert.Empty(QuickConnectMatcher.Match("COM", "ssh", sources));
	}

	[Fact]
	public void Match_CompletionReadsBackAsTheSameTarget()
	{
		HostProfile v6 = new() { Id = Guid.NewGuid(), Address = "fe80::1" };
		ConnectionCatalog catalog = new([], [v6], [Login(v6, "ssh", "root") with { Port = 2200 }]);

		QuickConnectRow row = Assert.Single(QuickConnectMatcher.Match("fe80", "ssh", Sources(catalog)));

		Assert.Equal("root@[fe80::1]:2200", row.Completion);
		Assert.True(QuickConnectTarget.TryParse(row.Completion, out QuickConnectTarget? target));
		Assert.Equal(new QuickConnectTarget(null, "root", "fe80::1", 2200), target);
	}

	[Theory]
	[InlineData("prod-db-2", "db", QuickConnectRank.WordPrefix)]
	[InlineData("prod-db-2", "rod", QuickConnectRank.Contains)]
	[InlineData("prod-db-2", "PROD", QuickConnectRank.Prefix)]
	[InlineData("prod-db-2", "prod-db-2", QuickConnectRank.Exact)]
	[InlineData("prod-db-2", "x", QuickConnectRank.None)]
	[InlineData("ab-ab", "ab", QuickConnectRank.Prefix)]
	[InlineData("xab-ab", "ab", QuickConnectRank.WordPrefix)]
	[InlineData(null, "ab", QuickConnectRank.None)]
	public void RankOf_ReadsWordsAtEverySeparator(string? field, string needle, QuickConnectRank rank) =>
		Assert.Equal(rank, QuickConnectMatcher.RankOf(field, needle));

	private static ConnectionProfile Login(HostProfile host, string protocol, string? user) => new()
	{
		Id = Guid.NewGuid(),
		HostId = host.Id,
		ProtocolId = protocol,
		Username = user,
	};

	private static QuickConnectRecent Recent(string protocol, string? user, string address, int? port) => new()
	{
		ProtocolId = protocol,
		Username = user,
		Address = address,
		Port = port,
		ConnectedAt = DateTimeOffset.UnixEpoch,
	};

	private static QuickConnectSources Sources(
		ConnectionCatalog catalog,
		IReadOnlyList<QuickConnectRecent>? recents = null,
		string? detectedProtocol = null,
		IReadOnlyList<QuickConnectSuggestion>? detected = null) => new()
	{
		Catalog = catalog,
		Recents = recents ?? [],
		DetectedProtocolId = detectedProtocol,
		Detected = detected ?? [],
		Protocols = Descriptors(new QuickConnectProtocols().Protocols.ToArray()),
	};

	private static Dictionary<string, ProtocolDescriptor> Descriptors(params ProtocolDescriptor[] protocols) =>
		protocols.ToDictionary(protocol => protocol.Id, StringComparer.OrdinalIgnoreCase);
}
