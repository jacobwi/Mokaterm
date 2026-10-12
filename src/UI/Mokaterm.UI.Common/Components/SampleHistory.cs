using System.Collections;

namespace Mokaterm.UI.Common.Components;

/// <summary>
/// The newest samples of one figure, oldest first, as a <see cref="Sparkline"/> draws them: a container's CPU, a
/// machine's memory or network traffic. Past <see cref="Capacity"/> the oldest sample makes room for the new one.
/// </summary>
/// <remarks>
/// Hand the same instance to the <see cref="Sparkline"/> on every render: a parameter of this type counts as changed
/// whenever its parent renders, so the line follows what was added since.
/// </remarks>
public sealed class SampleHistory : IReadOnlyList<double>
{
	private readonly double[] _values;
	private int _start;

	public SampleHistory(int capacity)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
		_values = new double[capacity];
	}

	/// <summary>How many samples it keeps; a <see cref="Sparkline"/> showing it takes this as its <c>Slots</c>.</summary>
	public int Capacity => _values.Length;

	public int Count { get; private set; }

	/// <summary>The newest sample, or null before the first.</summary>
	public double? Latest => Count == 0 ? null : this[Count - 1];

	public double this[int index]
	{
		get
		{
			ArgumentOutOfRangeException.ThrowIfNegative(index);
			ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
			return _values[(_start + index) % _values.Length];
		}
	}

	public void Add(double value)
	{
		if (Count < _values.Length)
		{
			_values[(_start + Count) % _values.Length] = value;
			Count++;
			return;
		}

		_values[_start] = value;
		_start = (_start + 1) % _values.Length;
	}

	public void Clear()
	{
		_start = 0;
		Count = 0;
	}

	public IEnumerator<double> GetEnumerator()
	{
		for (int index = 0; index < Count; index++)
		{
			yield return this[index];
		}
	}

	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
