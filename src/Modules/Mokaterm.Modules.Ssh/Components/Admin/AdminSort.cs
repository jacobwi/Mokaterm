using Moka.Red.Data.Table;

namespace Mokaterm.Modules.Ssh.Components.Admin;

/// <summary>Which column a list is sorted by. Clicking the same header again turns the order around.</summary>
public sealed class AdminSort
{
	public AdminSort(string key, bool descending = false)
	{
		Key = key;
		Descending = descending;
	}

	public string Key { get; private set; }

	public bool Descending { get; private set; }

	/// <summary>What a <c>SortHeader</c> for <paramref name="key"/> draws: the direction while it is the sorted column.</summary>
	public MokaSortDirection DirectionOf(string key) =>
		Key != key ? MokaSortDirection.None
		: Descending ? MokaSortDirection.Descending
		: MokaSortDirection.Ascending;

	/// <summary>Sorts by <paramref name="key"/>; numbers usually start with the largest, names with A.</summary>
	public void Toggle(string key, bool startDescending)
	{
		if (Key == key)
		{
			Descending = !Descending;
			return;
		}

		Key = key;
		Descending = startDescending;
	}

	public IEnumerable<T> Apply<T>(IEnumerable<T> items, Func<T, string> key) => Descending
		? items.OrderByDescending(key, StringComparer.OrdinalIgnoreCase)
		: items.OrderBy(key, StringComparer.OrdinalIgnoreCase);

	public IEnumerable<T> Apply<T>(IEnumerable<T> items, Func<T, double> key) => Descending
		? items.OrderByDescending(key)
		: items.OrderBy(key);
}
