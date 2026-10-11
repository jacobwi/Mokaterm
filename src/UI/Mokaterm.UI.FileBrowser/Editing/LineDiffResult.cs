namespace Mokaterm.UI.FileBrowser.Editing;

/// <summary>The rows <see cref="LineDiff"/> found, or its verdict that the two texts were too far apart to compare.</summary>
internal sealed class LineDiffResult
{
	private LineDiffResult(IReadOnlyList<DiffLine> lines, int added, int removed, bool tooDifferent)
	{
		Lines = lines;
		Added = added;
		Removed = removed;
		IsTooDifferent = tooDifferent;
	}

	/// <summary>The search ran out of its budget before it found the shortest set of changes, so there are no rows.</summary>
	public static LineDiffResult TooDifferent { get; } = new([], 0, 0, tooDifferent: true);

	/// <summary>Changes with up to <see cref="LineDiff.ContextLines"/> unchanged lines around each; empty when nothing changed.</summary>
	public IReadOnlyList<DiffLine> Lines { get; }

	public int Added { get; }

	public int Removed { get; }

	public bool IsTooDifferent { get; }

	public bool HasChanges => Added > 0 || Removed > 0;

	internal static LineDiffResult Of(IReadOnlyList<DiffLine> lines, int added, int removed) =>
		new(lines, added, removed, tooDifferent: false);
}
