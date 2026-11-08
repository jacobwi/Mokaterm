using System.Collections;

namespace Mokaterm.UI.Common.Components;

/// <summary>
/// The newest samples of one figure, oldest first, as a <see cref="Sparkline"/> or a live <c>MokaLineChart</c> draws
/// them (the chart through <see cref="Window"/>): a container's CPU, a machine's memory or network traffic. Past
/// <see cref="Capacity"/> the oldest sample makes room for the new one.
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

	/// <summary>
	/// <paramref name="values"/> as the window a live Moka.Red chart draws, at least <paramref name="slots"/> long and
	/// newest last. While there are fewer values the front is <see cref="double.NaN"/>, which those charts leave as a
	/// gap, so a line that has only started grows in from the right edge instead of stretching over the width. The
	/// values themselves are copied as they are.
	/// </summary>
	public static double[] Window(IReadOnlyList<double> values, int slots)
	{
		ArgumentNullException.ThrowIfNull(values);
		int length = Math.Max(Math.Max(slots, 0), values.Count);
		double[] window = new double[length];
		int gap = length - values.Count;
		Array.Fill(window, double.NaN, 0, gap);
		for (int index = 0; index < values.Count; index++)
		{
			window[gap + index] = values[index];
		}

		return window;
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
