namespace Mokaterm.Modules.Rdp.Protocol;

/// <summary>
/// Collects the rectangles a server redrew between two frames. Everything could be merged into one union, but a
/// menu opening in one corner and a clock ticking in the other would then repaint the whole desktop, so a few
/// rectangles are kept and only the cheapest merge is taken when a new one arrives.
/// </summary>
internal sealed class RdpDirtyRegions
{
	/// <summary>More than this and the extra messages cost more than the pixels they save.</summary>
	public const int MaxRegions = 4;

	private readonly List<RdpRegion> _regions = new(MaxRegions);

	public bool IsEmpty => _regions.Count == 0;

	public void Add(RdpRegion region)
	{
		if (region.IsEmpty)
		{
			return;
		}

		for (int i = 0; i < _regions.Count; i++)
		{
			// A rectangle already covered by one we hold adds nothing.
			if (_regions[i].Union(region) == _regions[i])
			{
				return;
			}
		}

		_regions.Add(region);
		while (_regions.Count > MaxRegions)
		{
			MergeCheapestPair();
		}
	}

	/// <summary>Returns what has piled up and starts again. The list is a copy the caller owns.</summary>
	public IReadOnlyList<RdpRegion> Take()
	{
		RdpRegion[] taken = [.. _regions];
		_regions.Clear();
		return taken;
	}

	public void Clear() => _regions.Clear();

	private void MergeCheapestPair()
	{
		int bestLeft = 0;
		int bestRight = 1;
		long bestCost = long.MaxValue;
		for (int i = 0; i < _regions.Count; i++)
		{
			for (int j = i + 1; j < _regions.Count; j++)
			{
				// The cost of a merge is the area it adds over keeping both.
				long cost = _regions[i].Union(_regions[j]).Area - _regions[i].Area - _regions[j].Area;
				if (cost < bestCost)
				{
					bestCost = cost;
					bestLeft = i;
					bestRight = j;
				}
			}
		}

		_regions[bestLeft] = _regions[bestLeft].Union(_regions[bestRight]);
		_regions.RemoveAt(bestRight);
	}
}
