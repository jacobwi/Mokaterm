using System.Text;

namespace Mokaterm.UI.Workspace;

/// <summary>
/// What can be done to a pane tree. Panes are numbered in the order this walks them, first half before second half,
/// which for every named layout is reading order: left to right, then top to bottom.
/// </summary>
internal static class PaneLayout
{
	// Reading recurses once per split before it meets a single pane, and a stack overflow cannot be caught, so a
	// hand-edited file is cut off by length. Four panes never take more than 16 characters.
	private const int MaxCodeLength = 32;

	/// <summary>The pane ids in order.</summary>
	public static IReadOnlyList<int> Panes(PaneNode node)
	{
		List<int> panes = [];
		Collect(node, panes);
		return panes;
	}

	/// <summary>Where <paramref name="paneId"/> comes in <paramref name="panes"/>, or -1.</summary>
	public static int IndexOf(IReadOnlyList<int> panes, int paneId)
	{
		ArgumentNullException.ThrowIfNull(panes);
		for (int i = 0; i < panes.Count; i++)
		{
			if (panes[i] == paneId)
			{
				return i;
			}
		}

		return -1;
	}

	public static bool Contains(PaneNode node, int paneId) => node switch
	{
		PaneLeaf leaf => leaf.PaneId == paneId,
		PaneSplit split => Contains(split.First, paneId) || Contains(split.Second, paneId),
		_ => false,
	};

	/// <summary>How many panes a named layout has.</summary>
	public static int PaneCount(PaneArrangement arrangement) => arrangement switch
	{
		PaneArrangement.Single => 1,
		PaneArrangement.SideBySide or PaneArrangement.Stacked => 2,
		PaneArrangement.MainAndStack => 3,
		_ => 4,
	};

	/// <summary>The named layout with each layout's pane count, the one a pane past the last one grows into.</summary>
	public static PaneArrangement ForCount(int panes) => panes switch
	{
		<= 1 => PaneArrangement.Single,
		2 => PaneArrangement.SideBySide,
		3 => PaneArrangement.MainAndStack,
		_ => PaneArrangement.Grid,
	};

	/// <summary>
	/// Builds a named layout over <paramref name="paneIds"/>, which must hold <see cref="PaneCount"/> ids, in pane order.
	/// </summary>
	public static PaneNode Build(PaneArrangement arrangement, IReadOnlyList<int> paneIds, Func<int> nextSplitId)
	{
		ArgumentNullException.ThrowIfNull(paneIds);
		ArgumentNullException.ThrowIfNull(nextSplitId);
		if (paneIds.Count < PaneCount(arrangement))
		{
			throw new ArgumentException("Every pane of the layout needs an id.", nameof(paneIds));
		}

		PaneLeaf Leaf(int index) => new(paneIds[index]);

		return arrangement switch
		{
			PaneArrangement.Single => Leaf(0),
			PaneArrangement.SideBySide => new PaneSplit(nextSplitId(), PaneAxis.SideBySide, Leaf(0), Leaf(1)),
			PaneArrangement.Stacked => new PaneSplit(nextSplitId(), PaneAxis.Stacked, Leaf(0), Leaf(1)),
			PaneArrangement.MainAndStack => new PaneSplit(
				nextSplitId(),
				PaneAxis.SideBySide,
				Leaf(0),
				new PaneSplit(nextSplitId(), PaneAxis.Stacked, Leaf(1), Leaf(2))),

			// Rows first, so the panes number in reading order.
			_ => new PaneSplit(
				nextSplitId(),
				PaneAxis.Stacked,
				new PaneSplit(nextSplitId(), PaneAxis.SideBySide, Leaf(0), Leaf(1)),
				new PaneSplit(nextSplitId(), PaneAxis.SideBySide, Leaf(2), Leaf(3))),
		};
	}

	/// <summary>Opens <paramref name="newPaneId"/> on the <paramref name="direction"/> side of <paramref name="paneId"/>.</summary>
	public static PaneNode Split(PaneNode node, int paneId, PaneDirection direction, int newPaneId, int splitId) => node switch
	{
		PaneLeaf leaf when leaf.PaneId == paneId => direction switch
		{
			PaneDirection.Right => new PaneSplit(splitId, PaneAxis.SideBySide, leaf, new PaneLeaf(newPaneId)),
			PaneDirection.Left => new PaneSplit(splitId, PaneAxis.SideBySide, new PaneLeaf(newPaneId), leaf),
			PaneDirection.Down => new PaneSplit(splitId, PaneAxis.Stacked, leaf, new PaneLeaf(newPaneId)),
			_ => new PaneSplit(splitId, PaneAxis.Stacked, new PaneLeaf(newPaneId), leaf),
		},
		PaneSplit split => Rebuild(
			split,
			Split(split.First, paneId, direction, newPaneId, splitId),
			Split(split.Second, paneId, direction, newPaneId, splitId)),
		_ => node,
	};

	/// <summary>
	/// The tree without the panes <paramref name="keep"/> turns down, each split that lost a half replaced by the other
	/// half. Null when no pane is kept.
	/// </summary>
	public static PaneNode? Prune(PaneNode node, Func<int, bool> keep)
	{
		ArgumentNullException.ThrowIfNull(keep);
		switch (node)
		{
			case PaneLeaf leaf:
				return keep(leaf.PaneId) ? leaf : null;
			case PaneSplit split:
				PaneNode? first = Prune(split.First, keep);
				PaneNode? second = Prune(split.Second, keep);
				if (first is null || second is null)
				{
					return first ?? second;
				}

				return Rebuild(split, first, second);
			default:
				return null;
		}
	}

	/// <summary>
	/// The first pane of whatever sits beside <paramref name="paneId"/> in its split: the pane that takes over the space
	/// when this one goes. Null for a pane that is the whole tree, or is not in it.
	/// </summary>
	public static int? Neighbour(PaneNode node, int paneId)
	{
		if (node is not PaneSplit split)
		{
			return null;
		}

		if (split.First is PaneLeaf first && first.PaneId == paneId)
		{
			return Panes(split.Second)[0];
		}

		if (split.Second is PaneLeaf second && second.PaneId == paneId)
		{
			return Panes(split.First)[^1];
		}

		return Neighbour(split.First, paneId) ?? Neighbour(split.Second, paneId);
	}

	/// <summary>
	/// The shape of a tree without its ids, as the plain <c>ui-state</c> document stores it: <c>P</c> is a pane,
	/// <c>H(a,b)</c> two halves side by side and <c>V(a,b)</c> two halves stacked.
	/// </summary>
	public static string Write(PaneNode node)
	{
		StringBuilder code = new();
		Write(node, code);
		return code.ToString();
	}

	/// <summary>
	/// Reads what <see cref="Write"/> wrote, giving every pane and split a fresh id. Null for anything that is not a tree
	/// of one to <paramref name="maxPanes"/> panes: the file can be edited by hand, and a bad layout only costs the layout.
	/// </summary>
	public static PaneNode? Read(string? code, int maxPanes, Func<int> nextPaneId, Func<int> nextSplitId)
	{
		ArgumentNullException.ThrowIfNull(nextPaneId);
		ArgumentNullException.ThrowIfNull(nextSplitId);
		if (string.IsNullOrEmpty(code) || code.Length > MaxCodeLength)
		{
			return null;
		}

		int index = 0;
		int panes = 0;
		PaneNode? node = ReadNode(code, ref index, ref panes, maxPanes, nextPaneId, nextSplitId);
		return node is not null && index == code.Length ? node : null;
	}

	private static PaneNode? ReadNode(string code, ref int index, ref int panes, int maxPanes, Func<int> nextPaneId, Func<int> nextSplitId)
	{
		if (index >= code.Length)
		{
			return null;
		}

		char kind = code[index++];
		if (kind == 'P')
		{
			return ++panes > maxPanes ? null : new PaneLeaf(nextPaneId());
		}

		if ((kind != 'H' && kind != 'V') || !Expect(code, ref index, '('))
		{
			return null;
		}

		PaneNode? first = ReadNode(code, ref index, ref panes, maxPanes, nextPaneId, nextSplitId);
		if (first is null || !Expect(code, ref index, ','))
		{
			return null;
		}

		PaneNode? second = ReadNode(code, ref index, ref panes, maxPanes, nextPaneId, nextSplitId);
		if (second is null || !Expect(code, ref index, ')'))
		{
			return null;
		}

		return new PaneSplit(nextSplitId(), kind == 'H' ? PaneAxis.SideBySide : PaneAxis.Stacked, first, second);
	}

	private static bool Expect(string code, ref int index, char expected)
	{
		if (index >= code.Length || code[index] != expected)
		{
			return false;
		}

		index++;
		return true;
	}

	private static void Write(PaneNode node, StringBuilder code)
	{
		if (node is PaneSplit split)
		{
			code.Append(split.Axis == PaneAxis.SideBySide ? "H(" : "V(");
			Write(split.First, code);
			code.Append(',');
			Write(split.Second, code);
			code.Append(')');
		}
		else
		{
			code.Append('P');
		}
	}

	private static void Collect(PaneNode node, List<int> panes)
	{
		switch (node)
		{
			case PaneLeaf leaf:
				panes.Add(leaf.PaneId);
				break;
			case PaneSplit split:
				Collect(split.First, panes);
				Collect(split.Second, panes);
				break;
			default:
				break;
		}
	}

	/// <summary>The same split over new halves, or the split itself when neither half changed.</summary>
	private static PaneSplit Rebuild(PaneSplit split, PaneNode first, PaneNode second) =>
		ReferenceEquals(first, split.First) && ReferenceEquals(second, split.Second)
			? split
			: split with { First = first, Second = second };
}
