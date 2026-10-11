using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.UI.FileBrowser.Browsing;

/// <summary>Runs the steps of a paste or a duplicate on the server.</summary>
internal static class EntryCopier
{
	/// <summary>
	/// Copies each step not yet in <paramref name="done"/> and adds it there once it went through, stopping at the first
	/// failure. Running the same steps again, as a retry as root does, therefore repeats only what is left: a copy made the
	/// first time would otherwise now stand in the way of itself.
	/// </summary>
	public static async Task CopyAsync(IRemoteFileSystem fileSystem, IReadOnlyList<PasteStep> steps, ISet<int> done, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(fileSystem);
		ArgumentNullException.ThrowIfNull(steps);
		ArgumentNullException.ThrowIfNull(done);
		for (int index = 0; index < steps.Count; index++)
		{
			if (done.Contains(index))
			{
				continue;
			}

			cancellationToken.ThrowIfCancellationRequested();
			PasteStep step = steps[index];
			await fileSystem.CopyAsync(step.Source.Path, step.Destination, step.Overwrite, cancellationToken);
			done.Add(index);
		}
	}
}
