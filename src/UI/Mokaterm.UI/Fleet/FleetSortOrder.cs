using Moka.Red.Data.Table;

namespace Mokaterm.UI.Fleet;

/// <summary>
/// The column the fleet overview is sorted by and which way. The default is no sort at all: the overview's own order,
/// machines by name and their logins under them, as the tree has them.
/// </summary>
internal readonly record struct FleetSortOrder(FleetColumn Column, MokaSortDirection Direction)
{
	public static FleetSortOrder Natural => default;

	public bool IsNatural => Direction == MokaSortDirection.None;
}
