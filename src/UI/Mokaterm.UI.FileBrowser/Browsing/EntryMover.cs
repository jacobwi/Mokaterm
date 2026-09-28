using Mokaterm.Abstractions.FileSystem;
using Mokaterm.UI.FileBrowser.Transfers;

namespace Mokaterm.UI.FileBrowser.Browsing;

/// <summary>Moves entries into a folder, the way dragging them onto a folder row does.</summary>
internal static class EntryMover
{
	/// <summary>
	/// Moves each entry into <paramref name="targetDirectory"/>, asking <paramref name="resolver"/> about names already taken
	/// there. Stops at the first failure, and when the user cancels at a prompt.
	/// </summary>
	public static async Task MoveAsync(
		IRemoteFileSystem fileSystem,
		IReadOnlyList<RemoteFileEntry> entries,
		string targetDirectory,
		OverwriteResolver resolver,
		CancellationToken cancellationToken)
	{
		foreach (RemoteFileEntry entry in entries)
		{
			string destination = RemotePath.Combine(targetDirectory, entry.Name);
			try
			{
				await fileSystem.RenameAsync(entry.Path, destination, overwrite: false, cancellationToken);
			}
			catch (RemoteFileSystemException ex) when (ex.Kind == RemoteFileErrorKind.AlreadyExists)
			{
				RemoteFileEntry? existing = await fileSystem.StatAsync(destination, cancellationToken);
				if (existing is null)
				{
					throw;
				}

				// A link can lead back to the folder the entry is already in. What is "already there" is then the entry
				// itself, and replacing it can delete it: without an atomic replace, the destination goes before the rename.
				if (await IsSameFolderAsync(fileSystem, RemotePath.GetParent(entry.Path), targetDirectory, cancellationToken))
				{
					continue;
				}

				long? size = entry.Kind == RemoteEntryKind.File ? entry.Size : null;
				OverwriteChoice choice = await resolver.ResolveAsync(destination, existing, size, entry.LastModified, cancellationToken);
				if (choice == OverwriteChoice.Cancel)
				{
					return;
				}

				if (choice == OverwriteChoice.Overwrite)
				{
					await fileSystem.RenameAsync(entry.Path, destination, overwrite: true, cancellationToken);
				}
			}
		}
	}

	private static async Task<bool> IsSameFolderAsync(IRemoteFileSystem fileSystem, string first, string second, CancellationToken cancellationToken)
	{
		try
		{
			string resolvedFirst = await fileSystem.ResolvePathAsync(first, cancellationToken);
			string resolvedSecond = await fileSystem.ResolvePathAsync(second, cancellationToken);
			return string.Equals(RemotePath.Normalize(resolvedFirst), RemotePath.Normalize(resolvedSecond), StringComparison.Ordinal);
		}
		catch (RemoteFileSystemException)
		{
			// Not known to be the same; the overwrite prompt decides, as for any other name that is taken.
			return false;
		}
	}
}
