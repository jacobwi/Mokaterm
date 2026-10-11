namespace Mokaterm.UI.FileBrowser.Editing;

/// <summary>What one row of a <see cref="LineDiffResult"/> stands for.</summary>
internal enum DiffLineKind
{
	Unchanged,
	Added,
	Removed,

	/// <summary>A run of unchanged lines left out between the context kept around the changes.</summary>
	Skipped,
}
