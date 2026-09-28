using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.UI.FileBrowser.Properties;

/// <summary>
/// The state behind the properties dialog: what the selection holds, the edits made so far, and the change they add up
/// to, which holds only what differs unless a folder's values are pushed to everything inside it.
/// </summary>
internal sealed class PropertiesForm
{
	private const string RootAccount = "root";

	/// <param name="entries">The selection, at least one entry.</param>
	/// <param name="userName">The logged-in account.</param>
	/// <param name="asRoot">Start with root chosen, as when the browser already runs as root.</param>
	/// <param name="canElevate">The connection can run file operations as root.</param>
	public PropertiesForm(IReadOnlyList<RemoteFileEntry> entries, RemoteFileSystemFeatures features, string userName, bool asRoot, bool canElevate)
	{
		ArgumentNullException.ThrowIfNull(entries);
		ArgumentOutOfRangeException.ThrowIfZero(entries.Count);
		Entries = entries;
		Editable = [.. entries.Where(entry => entry.Kind != RemoteEntryKind.SymbolicLink)];
		UserName = userName;
		CanElevate = canElevate;
		AsRoot = asRoot && canElevate;
		SupportsOwnership = (features & RemoteFileSystemFeatures.Ownership) != 0;
		SupportsPermissions = (features & RemoteFileSystemFeatures.Permissions) != 0;
		HasFolders = Editable.Any(entry => entry.Kind == RemoteEntryKind.Directory);
		Owner = new AccountField(Editable.Select(entry => entry.Owner));
		Group = new AccountField(Editable.Select(entry => entry.Group));
		Mode = new ModeEditor(Editable.Select(entry => entry.Permissions));
		Location = CommonParent(entries);
	}

	public IReadOnlyList<RemoteFileEntry> Entries { get; }

	/// <summary>
	/// The entries owner and permission changes reach. Links are left out: chown and chmod would change what they point
	/// at, and SFTP has no way to change a link itself.
	/// </summary>
	public IReadOnlyList<RemoteFileEntry> Editable { get; }

	public int SkippedLinks => Entries.Count - Editable.Count;

	/// <summary>The only entry, or null for a selection of several.</summary>
	public RemoteFileEntry? Single => Entries.Count == 1 ? Entries[0] : null;

	/// <summary>The folder that holds the whole selection.</summary>
	public string Location { get; }

	public string UserName { get; }

	public bool IsRootUser => string.Equals(UserName, RootAccount, StringComparison.Ordinal);

	public bool CanElevate { get; }

	/// <summary>Apply as root. Only settable when <see cref="CanElevate"/>.</summary>
	public bool AsRoot { get; set; }

	public bool RunsAsRoot => IsRootUser || (AsRoot && CanElevate);

	public bool SupportsOwnership { get; }

	public bool SupportsPermissions { get; }

	public bool CanEditOwnership => SupportsOwnership && Editable.Count > 0;

	public bool CanEditPermissions => SupportsPermissions && Editable.Count > 0;

	public bool IsReadOnly => !CanEditOwnership && !CanEditPermissions;

	/// <summary>The selection has folders whose contents a change can reach.</summary>
	public bool HasFolders { get; }

	/// <summary>A selected folder can be sized by walking it.</summary>
	public bool CanMeasure => Entries.Any(entry => entry.Kind == RemoteEntryKind.Directory);

	/// <summary>The size of the selected regular files, without anything inside folders.</summary>
	public long SelectedFileBytes => Entries.Where(entry => entry.Kind == RemoteEntryKind.File).Sum(entry => Math.Max(entry.Size, 0));

	public AccountField Owner { get; }

	public AccountField Group { get; }

	public ModeEditor Mode { get; }

	/// <summary>Owner and group go to everything inside the selected folders, changed or not.</summary>
	public bool OwnershipRecursive { get; set; }

	/// <summary>The permissions shown go to everything inside the selected folders, changed or not.</summary>
	public bool PermissionsRecursive { get; set; }

	/// <summary>Which entries a recursive permission change reaches.</summary>
	public PermissionTargets Targets { get; set; }

	/// <summary>Execute bits of a recursive change only go to folders and to files that already have one.</summary>
	public bool ConditionalExecute { get; set; }

	public bool IsValid =>
		(!CanEditOwnership || (Owner.IsValid && Group.IsValid))
		&& (!CanEditPermissions || Mode.IsOctalValid);

	/// <summary>The owner the change sets, if any.</summary>
	public string? PendingOwner => CanEditOwnership ? Owner.ToApply(PushesOwnership) : null;

	/// <summary>The group the change sets, if any.</summary>
	public string? PendingGroup => CanEditOwnership ? Group.ToApply(PushesOwnership) : null;

	/// <summary>A new owner is about to be set without root, which only root may do.</summary>
	public bool NeedsRootForOwner => PendingOwner is not null && !RunsAsRoot;

	public bool HasChanges => BuildChange() is not null;

	private bool PushesOwnership => OwnershipRecursive && HasFolders;

	private bool PushesPermissions => PermissionsRecursive && HasFolders;

	/// <summary>The change to run, or null when nothing would change or a field is invalid.</summary>
	public PropertiesChange? BuildChange()
	{
		if (!IsValid)
		{
			return null;
		}

		string? owner = PendingOwner;
		string? group = PendingGroup;
		PermissionChange? permissions = null;
		if (CanEditPermissions && (Mode.IsChanged || PushesPermissions) && Mode.Known != UnixFileMode.None)
		{
			PermissionTargets targets = PushesPermissions ? Targets : PermissionTargets.All;
			bool conditionalExecute = PushesPermissions && ConditionalExecute && targets != PermissionTargets.Folders;
			permissions = Mode.ToChange(PushesPermissions, targets, conditionalExecute);
		}

		if (owner is null && group is null && permissions is null)
		{
			return null;
		}

		return new PropertiesChange
		{
			Owner = owner,
			Group = group,
			OwnershipRecursive = PushesOwnership && (owner is not null || group is not null),
			Permissions = permissions,
			AsRoot = AsRoot && CanElevate,
		};
	}

	private static string CommonParent(IReadOnlyList<RemoteFileEntry> entries)
	{
		string parent = RemotePath.GetParent(entries[0].Path);
		foreach (RemoteFileEntry entry in entries)
		{
			while (!RemotePath.IsSameOrInside(RemotePath.GetParent(entry.Path), parent))
			{
				parent = RemotePath.GetParent(parent);
			}
		}

		return parent;
	}
}
