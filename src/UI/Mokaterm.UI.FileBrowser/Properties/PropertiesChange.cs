using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.UI.FileBrowser.Properties;

/// <summary>What the properties dialog changes. Null members stay as they are.</summary>
internal sealed record PropertiesChange
{
	/// <summary>The new owner, or null to keep each owner.</summary>
	public string? Owner { get; init; }

	/// <summary>The new group, or null to keep each group.</summary>
	public string? Group { get; init; }

	/// <summary>Owner and group also go to everything inside the selected folders.</summary>
	public bool OwnershipRecursive { get; init; }

	/// <summary>The permission change; recursion in it reaches inside the selected folders only.</summary>
	public PermissionChange? Permissions { get; init; }

	/// <summary>Runs on the root file system instead of the logged-in user's.</summary>
	public bool AsRoot { get; init; }

	/// <summary>What the change does, for messages such as "Could not change the owner of app.log".</summary>
	public string Action
	{
		get
		{
			string? ownership = Owner is not null ? "owner" : Group is not null ? "group" : null;
			return (ownership, Permissions) switch
			{
				(null, _) => "change the permissions of",
				(_, null) => "change the " + ownership + " of",
				_ => "change the " + ownership + " and permissions of",
			};
		}
	}

	/// <summary>
	/// Changes each entry in turn, owner and group before permissions: a new owner can clear setuid and setgid bits that
	/// the permissions set again. Links are skipped, because both changes would reach what a link points at.
	/// </summary>
	public async Task ApplyAsync(IRemoteFileSystem fileSystem, IReadOnlyList<RemoteFileEntry> entries, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(fileSystem);
		ArgumentNullException.ThrowIfNull(entries);
		foreach (RemoteFileEntry entry in entries)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (entry.Kind == RemoteEntryKind.SymbolicLink)
			{
				continue;
			}

			bool folder = entry.Kind == RemoteEntryKind.Directory;
			if (Owner is { } owner)
			{
				await fileSystem.SetOwnerAsync(entry.Path, owner, Group, OwnershipRecursive && folder, cancellationToken);
			}
			else if (Group is { } group)
			{
				await fileSystem.SetGroupAsync(entry.Path, group, OwnershipRecursive && folder, cancellationToken);
			}

			if (Permissions is { } permissions)
			{
				await fileSystem.ChangePermissionsAsync(entry.Path, permissions with { Recursive = permissions.Recursive && folder }, cancellationToken);
			}
		}
	}
}
