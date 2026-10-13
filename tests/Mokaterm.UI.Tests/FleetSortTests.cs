using Moka.Red.Data.Table;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Fleet;
using Mokaterm.UI.Fleet;

namespace Mokaterm.UI.Tests;

/// <summary>How the fleet overview's columns order their rows, and what a header click does to the order.</summary>
public sealed class FleetSortTests
{
	private static readonly MokaSortDirection[] Directions = [MokaSortDirection.Ascending, MokaSortDirection.Descending];

	public static TheoryData<string> Columns => [.. Enum.GetNames<FleetColumn>()];

	[Fact]
	public void FirstClick_PutsTheMostPressingFiguresFirst_AndNamesFromA()
	{
		foreach (FleetColumn column in Enum.GetValues<FleetColumn>())
		{
			MokaSortDirection expected = column is FleetColumn.Login or FleetColumn.System
				? MokaSortDirection.Ascending
				: MokaSortDirection.Descending;
			Assert.Equal(expected, FleetSort.FirstDirection(column));
			Assert.Equal(new FleetSortOrder(column, expected), FleetSort.Next(FleetSortOrder.Natural, column));
		}
	}

	[Fact]
	public void Clicks_TurnTheColumnAround_ThenGoBackToTheOverviewsOwnOrder()
	{
		FleetSortOrder order = FleetSortOrder.Natural;

		order = FleetSort.Next(order, FleetColumn.Disk);
		Assert.Equal(new FleetSortOrder(FleetColumn.Disk, MokaSortDirection.Descending), order);
		order = FleetSort.Next(order, FleetColumn.Disk);
		Assert.Equal(new FleetSortOrder(FleetColumn.Disk, MokaSortDirection.Ascending), order);
		order = FleetSort.Next(order, FleetColumn.Disk);
		Assert.True(order.IsNatural);

		order = FleetSort.Next(order, FleetColumn.Login);
		Assert.Equal(new FleetSortOrder(FleetColumn.Login, MokaSortDirection.Ascending), order);
		order = FleetSort.Next(order, FleetColumn.Login);
		Assert.Equal(new FleetSortOrder(FleetColumn.Login, MokaSortDirection.Descending), order);
		order = FleetSort.Next(order, FleetColumn.Login);
		Assert.True(order.IsNatural);
	}

	[Fact]
	public void AnotherColumn_StartsAtItsOwnFirstDirection_WhateverTheLastOneDid()
	{
		FleetSortOrder disk = new(FleetColumn.Disk, MokaSortDirection.Ascending);

		Assert.Equal(new FleetSortOrder(FleetColumn.System, MokaSortDirection.Ascending), FleetSort.Next(disk, FleetColumn.System));
		Assert.Equal(new FleetSortOrder(FleetColumn.Updates, MokaSortDirection.Descending), FleetSort.Next(disk, FleetColumn.Updates));
	}

	[Fact]
	public void OnlyTheSortedColumn_HasADirection()
	{
		FleetSortOrder order = new(FleetColumn.Memory, MokaSortDirection.Descending);

		Assert.Equal(MokaSortDirection.Descending, FleetSort.DirectionOf(order, FleetColumn.Memory));
		Assert.Equal(MokaSortDirection.None, FleetSort.DirectionOf(order, FleetColumn.Disk));

		// The natural order's column is State by default, and still runs no way at all.
		Assert.Equal(MokaSortDirection.None, FleetSort.DirectionOf(FleetSortOrder.Natural, FleetColumn.State));
	}

	[Fact]
	public void NaturalOrder_LeavesTheRowsAsTheOverviewListedThem()
	{
		IReadOnlyList<FleetRow> rows = [Row("web10"), Row("web2"), Row("db")];

		Assert.Same(rows, FleetSort.Order(rows, FleetSortOrder.Natural));
	}

	/// <summary>
	/// For every column: the smaller figure, the larger one and a row without one. The larger one's machine sorts first
	/// by name and the one without a figure before both, so an order that fell back to names would fail.
	/// </summary>
	[Theory]
	[MemberData(nameof(Columns))]
	public void EveryColumn_SortsByWhatItMeans_WithRowsWithoutTheFigureLastBothWays(string name)
	{
		FleetColumn column = Enum.Parse<FleetColumn>(name);
		(FleetRow low, FleetRow high, FleetRow? none) = Pair(column);
		IReadOnlyList<FleetRow> rows = none is null ? [low, high] : [none, low, high];

		IReadOnlyList<FleetRow> ascending = FleetSort.Order(rows, new FleetSortOrder(column, MokaSortDirection.Ascending));
		IReadOnlyList<FleetRow> descending = FleetSort.Order(rows, new FleetSortOrder(column, MokaSortDirection.Descending));

		Assert.Equal(none is null ? [low, high] : [low, high, none], ascending);
		Assert.Equal(none is null ? [high, low] : [high, low, none], descending);
	}

	[Fact]
	public void State_RunsFromAFailedReadToAFinishedOne_WithLoginsNeverReadLast()
	{
		FleetRow failed = Row("a", HostRunState.Failed);
		FleetRow skipped = Row("b", HostRunState.Skipped);
		FleetRow cancelled = Row("c", HostRunState.Cancelled);
		FleetRow running = Row("d", HostRunState.Running);
		FleetRow connecting = Row("e", HostRunState.Connecting);
		FleetRow queued = Row("f", HostRunState.Queued);
		FleetRow done = Row("g", HostRunState.Done);
		FleetRow notRead = Row("0");
		IReadOnlyList<FleetRow> rows = [done, notRead, queued, failed, running, cancelled, connecting, skipped];

		Assert.Equal(
			[failed, skipped, cancelled, running, connecting, queued, done, notRead],
			FleetSort.Order(rows, new FleetSortOrder(FleetColumn.State, MokaSortDirection.Descending)));
		Assert.Equal(
			[done, queued, connecting, running, cancelled, skipped, failed, notRead],
			FleetSort.Order(rows, new FleetSortOrder(FleetColumn.State, MokaSortDirection.Ascending)));
	}

	[Fact]
	public void HostsThatWereSkippedOrFailed_WithNothingRead_GoLastInEitherDirection()
	{
		FleetRow full = Row("c", HostRunState.Done, new HostSummary { PendingUpdates = 7 });
		FleetRow empty = Row("d", HostRunState.Done, new HostSummary { PendingUpdates = 0 });
		FleetRow skipped = Row("a", HostRunState.Skipped);
		FleetRow failed = Row("b", HostRunState.Failed);

		// A failed read that kept its earlier summary still has its figure, as its dimmed row shows.
		FleetRow stale = Row("e", HostRunState.Failed, new HostSummary { PendingUpdates = 3 });
		IReadOnlyList<FleetRow> rows = [skipped, failed, full, empty, stale];

		Assert.Equal([full, stale, empty, skipped, failed], FleetSort.Order(rows, new FleetSortOrder(FleetColumn.Updates, MokaSortDirection.Descending)));
		Assert.Equal([empty, stale, full, skipped, failed], FleetSort.Order(rows, new FleetSortOrder(FleetColumn.Updates, MokaSortDirection.Ascending)));
	}

	[Fact]
	public void AnUnknownCount_IsNotZero()
	{
		FleetRow none = Row("b", HostRunState.Done, new HostSummary { ContainersRunning = 0, ContainersTotal = 0, PendingUpdates = 0 });

		// Docker answered half (or nothing) and the package manager said nothing: the column shows "-", not 0.
		FleetRow unknown = Row("a", HostRunState.Done, new HostSummary { ContainersRunning = 4 });
		IReadOnlyList<FleetRow> rows = [unknown, none];

		Assert.Equal([none, unknown], FleetSort.Order(rows, new FleetSortOrder(FleetColumn.Docker, MokaSortDirection.Ascending)));
		Assert.Equal([none, unknown], FleetSort.Order(rows, new FleetSortOrder(FleetColumn.Docker, MokaSortDirection.Descending)));
		Assert.Equal([none, unknown], FleetSort.Order(rows, new FleetSortOrder(FleetColumn.Updates, MokaSortDirection.Ascending)));
		Assert.Equal([none, unknown], FleetSort.Order(rows, new FleetSortOrder(FleetColumn.Updates, MokaSortDirection.Descending)));
	}

	[Fact]
	public void Updates_WithTheSameCount_PutTheMoreSecurityUpdatesFirst()
	{
		FleetRow plain = Row("a", HostRunState.Done, new HostSummary { PendingUpdates = 4, SecurityUpdates = 0 });
		FleetRow security = Row("b", HostRunState.Done, new HostSummary { PendingUpdates = 4, SecurityUpdates = 2 });
		FleetRow unknown = Row("0", HostRunState.Done, new HostSummary { PendingUpdates = 4 });
		IReadOnlyList<FleetRow> rows = [unknown, plain, security];

		Assert.Equal([security, plain, unknown], FleetSort.Order(rows, new FleetSortOrder(FleetColumn.Updates, MokaSortDirection.Descending)));
		Assert.Equal([plain, security, unknown], FleetSort.Order(rows, new FleetSortOrder(FleetColumn.Updates, MokaSortDirection.Ascending)));
	}

	[Fact]
	public void HostNames_SortNaturally()
	{
		FleetRow web10 = Row("web10");
		FleetRow web2 = Row("web2");
		FleetRow web1 = Row("web1");
		FleetRow web3 = Row("Web3");
		IReadOnlyList<FleetRow> rows = [web10, web2, web3, web1];

		Assert.Equal([web1, web2, web3, web10], FleetSort.Order(rows, new FleetSortOrder(FleetColumn.Login, MokaSortDirection.Ascending)));
		Assert.Equal([web10, web3, web2, web1], FleetSort.Order(rows, new FleetSortOrder(FleetColumn.Login, MokaSortDirection.Descending)));
	}

	[Fact]
	public void Login_SortsByWhatTheColumnShowsFirst_ThenTheUser()
	{
		HostProfile host = Host("db");
		FleetRow ops = new(host, Login(host, "ops"), null);
		FleetRow admin = new(host, Login(host, "admin"), null);
		FleetRow labelled = new(host, Login(host, "root") with { Label = "Backups" }, null);
		IReadOnlyList<FleetRow> rows = [ops, labelled, admin];

		Assert.Equal([labelled, admin, ops], FleetSort.Order(rows, new FleetSortOrder(FleetColumn.Login, MokaSortDirection.Ascending)));
	}

	[Fact]
	public void EqualFigures_StayInMachineNameOrder_InBothDirections()
	{
		HostSummary same = new() { FullestDisk = Disk(50) };
		FleetRow web10 = Row("web10", HostRunState.Done, same);
		FleetRow web2 = Row("web2", HostRunState.Done, same);
		FleetRow fuller = Row("zz", HostRunState.Done, new HostSummary { FullestDisk = Disk(80) });
		IReadOnlyList<FleetRow> rows = [web10, fuller, web2];

		Assert.Equal([fuller, web2, web10], FleetSort.Order(rows, new FleetSortOrder(FleetColumn.Disk, MokaSortDirection.Descending)));
		Assert.Equal([web2, web10, fuller], FleetSort.Order(rows, new FleetSortOrder(FleetColumn.Disk, MokaSortDirection.Ascending)));
	}

	[Fact]
	public void RowsThatTieOnEverything_KeepTheOverviewsOrder()
	{
		// Two logins of one machine, neither read: nothing tells them apart but the order they were listed in.
		HostProfile host = Host("web01");
		FleetRow first = new(host, Login(host, "root"), null);
		FleetRow second = new(host, Login(host, "deploy"), null);

		foreach (FleetColumn column in Enum.GetValues<FleetColumn>().Where(column => column != FleetColumn.Login))
		{
			foreach (MokaSortDirection direction in Directions)
			{
				Assert.Equal([first, second], FleetSort.Order([first, second], new FleetSortOrder(column, direction)));
				Assert.Equal([second, first], FleetSort.Order([second, first], new FleetSortOrder(column, direction)));
			}
		}
	}

	[Fact]
	public void Hold_KeepsTheShownRowsInPlace_AndPutsNewOnesAfterThem()
	{
		FleetRow a = Row("a");
		FleetRow b = Row("b");
		FleetRow c = Row("c");
		FleetRow d = Row("d");
		FleetRow e = Row("e");

		// The table showed c, a, b. Now the sort says b, d, a, e, and c is filtered away.
		IReadOnlyList<FleetRow> held = FleetSort.Hold([b, d, a, e], [c.Login.Id, a.Login.Id, b.Login.Id]);

		Assert.Equal([a, b, d, e], held);
		Assert.Equal([b, d, a, e], FleetSort.Hold([b, d, a, e], []));
	}

	private static (FleetRow Low, FleetRow High, FleetRow? None) Pair(FleetColumn column)
	{
		FleetRow none = Row("aaa");
		return column switch
		{
			FleetColumn.State => (Row("b", HostRunState.Done), Row("a", HostRunState.Failed), none),
			FleetColumn.Login => (Row("web2"), Row("web10"), null),
			FleetColumn.System => (
				Done("b", new HostSummary { OperatingSystem = "Debian GNU/Linux 9 (stretch)" }),
				Done("a", new HostSummary { OperatingSystem = "Debian GNU/Linux 12 (bookworm)" }),
				Done("aaa", new HostSummary { OperatingSystem = " " })),
			FleetColumn.Uptime => (
				Done("b", new HostSummary { Uptime = TimeSpan.FromHours(9) }),
				Done("a", new HostSummary { Uptime = TimeSpan.FromDays(10) }),
				none),

			// As text, 10.25 would come before 9.5 and 10% before 9%.
			FleetColumn.Load => (Done("b", new HostSummary { Load1 = 9.5 }), Done("a", new HostSummary { Load1 = 10.25 }), none),
			FleetColumn.Memory => (
				Done("b", new HostSummary { MemoryUsedBytes = 9, MemoryTotalBytes = 100 }),
				Done("a", new HostSummary { MemoryUsedBytes = 10, MemoryTotalBytes = 100 }),
				Done("aaa", new HostSummary { MemoryUsedBytes = 10 })),
			FleetColumn.Disk => (Done("b", new HostSummary { FullestDisk = Disk(9) }), Done("a", new HostSummary { FullestDisk = Disk(47) }), none),
			FleetColumn.Updates => (Done("b", new HostSummary { PendingUpdates = 9 }), Done("a", new HostSummary { PendingUpdates = 12 }), none),
			FleetColumn.Reboot => (
				Done("b", new HostSummary { RebootRequired = false }),
				Done("a", new HostSummary { RebootRequired = true }),
				Done("aaa", new HostSummary())),
			FleetColumn.Failed => (Done("b", new HostSummary { FailedServices = 2 }), Done("a", new HostSummary { FailedServices = 10 }), none),
			FleetColumn.Docker => (
				Done("b", new HostSummary { ContainersRunning = 2, ContainersTotal = 3 }),
				Done("a", new HostSummary { ContainersRunning = 10, ContainersTotal = 12 }),
				none),
			_ => throw new ArgumentOutOfRangeException(nameof(column), column, "Every column needs a pair here."),
		};
	}

	private static FleetRow Done(string hostName, HostSummary summary) => Row(hostName, HostRunState.Done, summary);

	private static FleetRow Row(string hostName, HostRunState? state = null, HostSummary? summary = null)
	{
		HostProfile host = Host(hostName);
		ConnectionProfile login = Login(host, "root");
		HostSurveyResult? result = state is { } runState
			? new HostSurveyResult
			{
				ConnectionId = login.Id,
				Title = login.GetTitle(host),
				HostName = host.DisplayName,
				Address = host.Address,
				State = runState,
				Summary = summary,
			}
			: null;
		return new FleetRow(host, login, result);
	}

	private static HostProfile Host(string name) => new() { Id = Guid.NewGuid(), Name = name, Address = "10.0.0.1" };

	private static ConnectionProfile Login(HostProfile host, string user) => new()
	{
		Id = Guid.NewGuid(),
		HostId = host.Id,
		ProtocolId = "ssh",
		Username = user,
	};

	private static HostDiskUsage Disk(int usedPercent) => new() { MountPoint = "/", UsedPercent = usedPercent };
}
