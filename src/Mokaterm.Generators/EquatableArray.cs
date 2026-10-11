using System.Collections;
using System.Collections.Immutable;

namespace Mokaterm.Generators;

/// <summary>
/// An immutable array that compares by its items. Incremental generators cache every step by equality, and an
/// <see cref="ImmutableArray{T}"/> compares by reference, which would rerun the output on every keystroke.
/// </summary>
internal readonly struct EquatableArray<T>(ImmutableArray<T> items) : IEquatable<EquatableArray<T>>, IEnumerable<T>
	where T : IEquatable<T>
{
	private readonly ImmutableArray<T> _items = items;

	public static EquatableArray<T> Empty => new(ImmutableArray<T>.Empty);

	public int Count => _items.IsDefault ? 0 : _items.Length;

	public T this[int index] => _items[index];

	public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right) => left.Equals(right);

	public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right) => !left.Equals(right);

	public bool Equals(EquatableArray<T> other)
	{
		if (Count != other.Count)
		{
			return false;
		}

		for (int index = 0; index < Count; index++)
		{
			if (!_items[index].Equals(other._items[index]))
			{
				return false;
			}
		}

		return true;
	}

	public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

	public override int GetHashCode()
	{
		int hash = 17;
		foreach (T item in this)
		{
			hash = unchecked((hash * 31) + item.GetHashCode());
		}

		return hash;
	}

	public IEnumerator<T> GetEnumerator() =>
		((IEnumerable<T>)(_items.IsDefault ? ImmutableArray<T>.Empty : _items)).GetEnumerator();

	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
