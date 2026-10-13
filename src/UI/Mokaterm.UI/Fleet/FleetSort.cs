using System.Globalization;
using Moka.Red.Data.Table;
using Mokaterm.Abstractions.Commands;
using Mokaterm.UI.Presentation;

namespace Mokaterm.UI.Fleet;

/// <summary>
/// How the fleet overview orders its rows: by what a column means rather than by the text it shows, so 10 comes after
/// 9, web10 after web2 and a reboot that is needed before one that is not. A row without the figure (not read yet,
/// skipped, failed, or a count the machine did not give) goes last whichever way the column runs, because turning a
/// column around is a request for the other end of the figures, not for the rows that have none. Rows with the same
/// figure stay in machine name order in both directions.
/// </summary>
internal static class FleetSort
{
	/// <summary>Digits compare as numbers (web2 before web10), and case does not count.</summary>
	private const CompareOptions NaturalText = CompareOptions.NumericOrdering | CompareOptions.IgnoreCase;

	/// <summary>
	/// The direction a column's first click picks: names from A, and figures with the most pressing first (the fullest
	/// disk, the most updates, a reboot needed, a failed read), which is what a fleet is scanned for.
	/// </summary>
	public static MokaSortDirection FirstDirection(FleetColumn column) =>
		column is FleetColumn.Login or FleetColumn.System ? MokaSortDirection.Ascending : MokaSortDirection.Descending;

	/// <summary>
	/// The order after a click on <paramref name="column"/>: another column starts at its first direction, a second click
	/// turns it around, and a third goes back to the overview's own order.
	/// </summary>
	public static FleetSortOrder Next(FleetSortOrder current, FleetColumn column)
	{
		MokaSortDirection first = FirstDirection(column);
		if (current.IsNatural || current.Column != column)
		{
			return new FleetSortOrder(column, first);
		}

		return current.Direction == first ? current with { Direction = Reverse(first) } : FleetSortOrder.Natural;
	}

	/// <summary>Which way <paramref name="column"/> runs under <paramref name="order"/>; None for every other column.</summary>
	public static MokaSortDirection DirectionOf(FleetSortOrder order, FleetColumn column) =>
		order.Column == column ? order.Direction : MokaSortDirection.None;

	/// <summary><paramref name="rows"/> in <paramref name="order"/>, or as they came for the natural order.</summary>
	public static IReadOnlyList<FleetRow> Order(IReadOnlyList<FleetRow> rows, FleetSortOrder order)
	{
		ArgumentNullException.ThrowIfNull(rows);
		if (order.IsNatural)
		{
			return rows;
		}

		bool descending = order.Direction == MokaSortDirection.Descending;
		Comparer<FleetRow> comparer = Comparer<FleetRow>.Create((x, y) => Compare(x, y, order.Column, descending));

		// OrderBy is stable, so rows that tie on the figure and the machine's name keep the overview's own order.
		return [.. rows.OrderBy(row => row, comparer)];
	}

	/// <summary>
	/// <paramref name="rows"/> with every login in <paramref name="shown"/> kept in the place it had, and any other row
	/// after them in its own order. This is what the table draws while the pointer rests on it, so a host that finishes
	/// its read does not move under the pointer and a click lands on the row it was aimed at.
	/// </summary>
	public static IReadOnlyList<FleetRow> Hold(IReadOnlyList<FleetRow> rows, IReadOnlyList<Guid> shown)
	{
		ArgumentNullException.ThrowIfNull(rows);
		ArgumentNullException.ThrowIfNull(shown);
		Dictionary<Guid, int> places = new(shown.Count);
		for (int i = 0; i < shown.Count; i++)
		{
			_ = places.TryAdd(shown[i], i);
		}

		return
		[
			.. rows
				.Select((row, index) => (Row: row, Place: places.TryGetValue(row.Login.Id, out int place) ? place : shown.Count + index))
				.OrderBy(entry => entry.Place)
				.Select(entry => entry.Row),
		];
	}

	/// <summary>
	/// Compares two rows by <paramref name="column"/>: rows with the figure before rows without it in both directions,
	/// the figures themselves by <paramref name="descending"/>, then the machines' names from A.
	/// </summary>
	private static int Compare(FleetRow x, FleetRow y, FleetColumn column, bool descending)
	{
		int byFigure = column switch
		{
			FleetColumn.State => Figure(Severity(x), Severity(y), descending),
			FleetColumn.Login => LoginOrder(x, y, descending),
			FleetColumn.System => Text(x.Summary?.OperatingSystem, y.Summary?.OperatingSystem, descending),
			FleetColumn.Uptime => Figure(x.Summary?.Uptime, y.Summary?.Uptime, descending),
			FleetColumn.Load => Figure(x.Summary?.Load1, y.Summary?.Load1, descending),
			FleetColumn.Memory => Figure(x.Summary?.MemoryPercent, y.Summary?.MemoryPercent, descending),
			FleetColumn.Disk => Figure(x.Summary?.FullestDisk?.UsedPercent, y.Summary?.FullestDisk?.UsedPercent, descending),

			// The column shows the pending count with the security count beside it, so security only parts equal counts.
			FleetColumn.Updates => Then(
				Figure(x.Summary?.PendingUpdates, y.Summary?.PendingUpdates, descending),
				Figure(x.Summary?.SecurityUpdates, y.Summary?.SecurityUpdates, descending)),
			FleetColumn.Reboot => Figure(Reboot(x), Reboot(y), descending),
			FleetColumn.Failed => Figure(x.Summary?.FailedServices, y.Summary?.FailedServices, descending),
			FleetColumn.Docker => Then(
				Figure(Containers(x)?.Running, Containers(y)?.Running, descending),
				Figure(Containers(x)?.Total, Containers(y)?.Total, descending)),
			_ => 0,
		};

		return byFigure != 0 ? byFigure : Natural(x.Host.DisplayName, y.Host.DisplayName);
	}

	/// <summary>
	/// How much a row's state asks for a look, higher being worse; null for a login that was never read. A read under way
	/// sits between a finished one and one that did not get through.
	/// </summary>
	private static int? Severity(FleetRow row) => row.Result?.State switch
	{
		null => null,
		HostRunState.Done => 0,
		HostRunState.Queued => 1,
		HostRunState.Connecting => 2,
		HostRunState.Running => 3,
		HostRunState.Cancelled => 4,
		HostRunState.Skipped => 5,
		_ => 6,
	};

	private static int? Reboot(FleetRow row) => row.Summary?.RebootRequired switch
	{
		true => 1,
		false => 0,
		null => null,
	};

	/// <summary>Both counts or neither: the column shows nothing for a login Docker gave only half an answer to.</summary>
	private static (int Running, int Total)? Containers(FleetRow row) =>
		row.Summary is { ContainersRunning: { } running, ContainersTotal: { } total } ? (running, total) : null;

	/// <summary>What the login column shows first (the login's name or the machine's), then the user, then the address.</summary>
	private static int LoginOrder(FleetRow x, FleetRow y, bool descending) => Then(
		Text(SessionTitle.Name(x.Host, x.Login) ?? x.Host.Address, SessionTitle.Name(y.Host, y.Login) ?? y.Host.Address, descending),
		Then(Text(x.Login.Username, y.Login.Username, descending), Text(x.Host.Address, y.Host.Address, descending)));

	private static int Figure<T>(T? x, T? y, bool descending)
		where T : struct, IComparable<T>
	{
		if (x is not { } left || y is not { } right)
		{
			return Missing(x is null, y is null);
		}

		int compared = left.CompareTo(right);
		return descending ? -compared : compared;
	}

	private static int Text(string? x, string? y, bool descending)
	{
		bool xMissing = string.IsNullOrWhiteSpace(x);
		bool yMissing = string.IsNullOrWhiteSpace(y);
		if (xMissing || yMissing)
		{
			return Missing(xMissing, yMissing);
		}

		int compared = Natural(x!, y!);
		return descending ? -compared : compared;
	}

	/// <summary>A missing figure sorts after a present one, and two missing ones tie.</summary>
	private static int Missing(bool x, bool y) => x.CompareTo(y);

	private static int Then(int first, int second) => first != 0 ? first : second;

	private static int Natural(string x, string y) => CultureInfo.CurrentCulture.CompareInfo.Compare(x, y, NaturalText);

	private static MokaSortDirection Reverse(MokaSortDirection direction) =>
		direction == MokaSortDirection.Ascending ? MokaSortDirection.Descending : MokaSortDirection.Ascending;
}
