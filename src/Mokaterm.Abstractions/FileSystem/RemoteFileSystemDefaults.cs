namespace Mokaterm.Abstractions.FileSystem;

/// <summary>
/// What <see cref="IRemoteFileSystem"/> members with a default do when an implementation leaves them out, built only from
/// members every implementation has.
/// </summary>
internal static class RemoteFileSystemDefaults
{
	public static async ValueTask SetGroupAsync(IRemoteFileSystem fileSystem, string path, string group, bool recursive, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(group);
		RemoteFileEntry root = await StatAsync(fileSystem, path, cancellationToken);
		if (root.Kind == RemoteEntryKind.SymbolicLink)
		{
			// A link given directly changes what it points at, so that is whose owner stays.
			RemoteFileEntry target = await ResolveLinkAsync(fileSystem, root, cancellationToken);
			await fileSystem.SetOwnerAsync(root.Path, OwnerOf(target), group, recursive: false, cancellationToken);
			return;
		}

		await WalkAsync(
			fileSystem,
			root,
			recursive,
			entry => fileSystem.SetOwnerAsync(entry.Path, OwnerOf(entry), group, recursive: false, cancellationToken),
			cancellationToken);
	}

	public static async ValueTask ChangePermissionsAsync(IRemoteFileSystem fileSystem, string path, PermissionChange change, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(change);
		RemoteFileEntry root = await StatAsync(fileSystem, path, cancellationToken);
		if (!change.NeedsCurrentMode && change.Targets == PermissionTargets.All)
		{
			bool recursive = change.Recursive && root.Kind == RemoteEntryKind.Directory;
			await fileSystem.SetPermissionsAsync(root.Path, change.Mode & PermissionChange.AllBits, recursive, cancellationToken);
			return;
		}

		if (root.Kind == RemoteEntryKind.SymbolicLink)
		{
			if (change.Includes(root.Kind))
			{
				// The link's own mode says nothing; the new mode starts from what it points at.
				RemoteFileEntry target = await ResolveLinkAsync(fileSystem, root, cancellationToken);
				await fileSystem.SetPermissionsAsync(root.Path, NewMode(target, change), recursive: false, cancellationToken);
			}

			return;
		}

		await WalkAsync(
			fileSystem,
			root,
			change.Recursive,
			entry => change.Includes(entry.Kind)
				? fileSystem.SetPermissionsAsync(entry.Path, NewMode(entry, change), recursive: false, cancellationToken)
				: ValueTask.CompletedTask,
			cancellationToken);
	}

	/// <summary>Visits <paramref name="root"/> and, when recursive, everything below it, each folder before its contents.</summary>
	private static async ValueTask WalkAsync(
		IRemoteFileSystem fileSystem,
		RemoteFileEntry root,
		bool recursive,
		Func<RemoteFileEntry, ValueTask> visit,
		CancellationToken cancellationToken)
	{
		Stack<RemoteFileEntry> pending = new();
		pending.Push(root);
		while (pending.TryPop(out RemoteFileEntry? entry))
		{
			cancellationToken.ThrowIfCancellationRequested();
			await visit(entry);
			if (!recursive || entry.Kind != RemoteEntryKind.Directory)
			{
				continue;
			}

			foreach (RemoteFileEntry child in await fileSystem.ListAsync(entry.Path, cancellationToken))
			{
				if (child.Kind != RemoteEntryKind.SymbolicLink)
				{
					pending.Push(child);
				}
			}
		}
	}

	private static async ValueTask<RemoteFileEntry> StatAsync(IRemoteFileSystem fileSystem, string path, CancellationToken cancellationToken) =>
		await fileSystem.StatAsync(path, cancellationToken)
		?? throw new RemoteFileSystemException(RemoteFileErrorKind.NotFound, $"{path} does not exist.", path);

	private static async ValueTask<RemoteFileEntry> ResolveLinkAsync(IRemoteFileSystem fileSystem, RemoteFileEntry link, CancellationToken cancellationToken)
	{
		string resolved = await fileSystem.ResolvePathAsync(link.Path, cancellationToken);
		return await StatAsync(fileSystem, resolved, cancellationToken);
	}

	private static UnixFileMode NewMode(RemoteFileEntry entry, PermissionChange change)
	{
		UnixFileMode current = entry.Permissions
			?? (change.NeedsCurrentMode
				? throw new RemoteFileSystemException(
					RemoteFileErrorKind.NotSupported,
					$"The server does not report the permissions of {entry.Path}, so they cannot be changed in part.",
					entry.Path)
				: UnixFileMode.None);
		return change.Apply(current, entry.Kind == RemoteEntryKind.Directory);
	}

	private static string OwnerOf(RemoteFileEntry entry) => entry.Owner is { Length: > 0 } owner
		? owner
		: throw new RemoteFileSystemException(
			RemoteFileErrorKind.NotSupported,
			$"The server does not report the owner of {entry.Path}, so its group cannot be changed on its own.",
			entry.Path);
}
