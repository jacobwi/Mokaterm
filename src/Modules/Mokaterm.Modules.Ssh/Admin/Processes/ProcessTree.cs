namespace Mokaterm.Modules.Ssh.Admin.Processes;

/// <summary>Processes laid out by parent: each one under the process that started it, siblings in the list's own order.</summary>
internal static class ProcessTree
{
	/// <summary>
	/// Every process once, parents before their children, with how deep each sits. A process whose parent is not in the
	/// list (PID 1, the kernel's threads, one whose parent was missed or filtered out) starts a tree of its own. A loop
	/// in the parent links, which a PID reused between two reads can produce, cannot repeat or lose a process.
	/// </summary>
	/// <param name="order">Sorts one set of siblings, so the tree keeps the list's sort within each level.</param>
	public static IReadOnlyList<(ProcessInfo Process, int Depth)> Flatten(
		IReadOnlyList<ProcessInfo> processes,
		Func<IEnumerable<ProcessInfo>, IEnumerable<ProcessInfo>> order)
	{
		ArgumentNullException.ThrowIfNull(processes);
		ArgumentNullException.ThrowIfNull(order);
		Dictionary<int, ProcessInfo> byPid = [];
		foreach (ProcessInfo process in processes)
		{
			byPid.TryAdd(process.Pid, process);
		}

		Dictionary<int, List<ProcessInfo>> children = [];
		List<ProcessInfo> roots = [];
		foreach (ProcessInfo process in byPid.Values)
		{
			if (process.ParentPid is { } parent && parent != process.Pid && byPid.ContainsKey(parent))
			{
				if (!children.TryGetValue(parent, out List<ProcessInfo>? siblings))
				{
					siblings = [];
					children[parent] = siblings;
				}

				siblings.Add(process);
			}
			else
			{
				roots.Add(process);
			}
		}

		List<(ProcessInfo, int)> flat = new(byPid.Count);
		HashSet<int> placed = [];
		Walk(order(roots), 0);

		// Whatever sits on a loop of parent links has no root above it; each loop is entered at its lowest PID.
		foreach (ProcessInfo process in byPid.Values.OrderBy(process => process.Pid))
		{
			if (!placed.Contains(process.Pid))
			{
				Walk([process], 0);
			}
		}

		return flat;

		void Walk(IEnumerable<ProcessInfo> level, int depth)
		{
			Stack<(IEnumerator<ProcessInfo> Siblings, int Depth)> stack = new();
			stack.Push((level.GetEnumerator(), depth));
			while (stack.Count > 0)
			{
				(IEnumerator<ProcessInfo> siblings, int at) = stack.Peek();
				if (!siblings.MoveNext())
				{
					siblings.Dispose();
					stack.Pop();
					continue;
				}

				ProcessInfo process = siblings.Current;
				if (!placed.Add(process.Pid))
				{
					continue;
				}

				flat.Add((process, at));
				if (children.TryGetValue(process.Pid, out List<ProcessInfo>? below))
				{
					stack.Push((order(below).GetEnumerator(), at + 1));
				}
			}
		}
	}

	/// <summary>
	/// The processes that match, plus every ancestor of one, so a filtered tree still shows where each match sits.
	/// The ancestors that do not match themselves come back in <c>Context</c>.
	/// </summary>
	public static (IReadOnlyList<ProcessInfo> Shown, IReadOnlySet<int> Context) WithAncestors(
		IReadOnlyList<ProcessInfo> processes,
		Func<ProcessInfo, bool> matches)
	{
		ArgumentNullException.ThrowIfNull(processes);
		ArgumentNullException.ThrowIfNull(matches);
		Dictionary<int, ProcessInfo> byPid = [];
		foreach (ProcessInfo process in processes)
		{
			byPid.TryAdd(process.Pid, process);
		}

		HashSet<int> keep = [];
		HashSet<int> context = [];
		foreach (ProcessInfo process in byPid.Values.Where(matches))
		{
			keep.Add(process.Pid);
		}

		foreach (int pid in keep.ToList())
		{
			int? parent = byPid[pid].ParentPid;
			while (parent is { } up && byPid.TryGetValue(up, out ProcessInfo? ancestor) && keep.Add(up))
			{
				context.Add(up);
				parent = ancestor.ParentPid;
			}
		}

		return ([.. processes.Where(process => keep.Contains(process.Pid))], context);
	}
}
