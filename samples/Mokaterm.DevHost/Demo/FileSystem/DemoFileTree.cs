using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.DevHost.Demo.FileSystem;

/// <summary>
/// One session's in-memory POSIX tree, shared by its shell and its file browser. Every call checks Unix permissions for
/// the account it runs as and holds one lock, so the shell and parallel browser operations never see half a change.
/// Failures carry the messages the kernel would give (<c>No such file or directory</c>), which the shell prints as is.
/// </summary>
internal sealed class DemoFileTree
{
	/// <summary>Writes keep at most this many leading bytes; reads repeat them up to the file's size.</summary>
	public const int MaxStoredBytes = 64 * 1024;

	private const int MaxLinkHops = 40;
	private const UnixFileMode PermissionBits = (UnixFileMode)0xFFF;

	private readonly Lock _gate = new();
	private readonly DemoNode _root;
	private readonly HashSet<string> _users;
	private readonly HashSet<string> _groups;
	private readonly TimeProvider _timeProvider;

	public DemoFileTree(DemoNode root, IEnumerable<string> users, IEnumerable<string> groups, TimeProvider timeProvider)
	{
		_root = root;
		_users = new HashSet<string>(users, StringComparer.Ordinal);
		_groups = new HashSet<string>(groups, StringComparer.Ordinal);
		_timeProvider = timeProvider;
	}

	private enum Access
	{
		Search = 1,
		Write = 2,
		Read = 4,
	}

	private enum Failure
	{
		None,
		NotFound,
		NotDirectory,
		Denied,
		Loop,
	}

	private DateTimeOffset Now => _timeProvider.GetUtcNow();

	/// <summary>The canonical path with every symbolic link followed, like <c>realpath</c>.</summary>
	public string Resolve(string path, DemoAccount account)
	{
		lock (_gate)
		{
			return Walk(path, account, followLast: true).Path;
		}
	}

	/// <summary>Fails unless <paramref name="path"/> is a folder the account may enter, which is what <c>cd</c> checks.</summary>
	public void RequireEnterable(string path, DemoAccount account)
	{
		lock (_gate)
		{
			DemoNode node = Walk(path, account, followLast: true);
			if (node.Kind != RemoteEntryKind.Directory)
			{
				throw NotDirectory(path);
			}

			if (!Can(node, account, Access.Search))
			{
				throw Denied(path);
			}
		}
	}

	/// <summary>The entry itself, not what a link there points at (like <c>lstat</c>), or null when nothing is there.</summary>
	public RemoteFileEntry? Stat(string path, DemoAccount account)
	{
		string target = RemotePath.Normalize(path);
		lock (_gate)
		{
			if (target == RemotePath.Root)
			{
				return ToEntry(_root, target, account);
			}

			DemoNode? parent = TryWalk(RemotePath.GetParent(target), account, followLast: true, out Failure failure);
			if (parent is null)
			{
				if (failure is Failure.NotFound or Failure.NotDirectory)
				{
					return null;
				}

				throw ErrorFor(failure, target);
			}

			if (parent.Kind != RemoteEntryKind.Directory)
			{
				return null;
			}

			if (!Can(parent, account, Access.Search))
			{
				throw Denied(target);
			}

			return parent.Child(RemotePath.GetName(target)) is { } entry ? ToEntry(entry, target, account) : null;
		}
	}

	public List<RemoteFileEntry> ListDirectory(string path, DemoAccount account)
	{
		string directory = RemotePath.Normalize(path);
		lock (_gate)
		{
			DemoNode node = Walk(directory, account, followLast: true);
			if (node.Kind != RemoteEntryKind.Directory)
			{
				throw NotDirectory(directory);
			}

			if (!Can(node, account, Access.Read) || !Can(node, account, Access.Search))
			{
				throw Denied(directory);
			}

			return [.. node.Children.Select(child => ToEntry(child, RemotePath.Combine(directory, child.Name), account))];
		}
	}

	/// <summary>Reads a file, following links.</summary>
	public DemoFileData Read(string path, DemoAccount account)
	{
		string target = RemotePath.Normalize(path);
		lock (_gate)
		{
			DemoNode node = Walk(target, account, followLast: true);
			if (node.Kind == RemoteEntryKind.Directory)
			{
				throw IsDirectory(target);
			}

			if (!Can(node, account, Access.Read))
			{
				throw Denied(target);
			}

			return new DemoFileData(node.Size, node.Content, node.Modified);
		}
	}

	public void CreateDirectory(string path, DemoAccount account)
	{
		string target = RemotePath.Normalize(path);
		lock (_gate)
		{
			(DemoNode parent, DemoNode? existing, string name) = Locate(target, account);
			if (existing is not null)
			{
				throw AlreadyExists(target);
			}

			RequireChangeable(parent, account, target);
			parent.Add(DemoNode.NewDirectory(name, account.Name, account.Group, DemoModes.NewDirectory, Now));
			parent.Modified = Now;
		}
	}

	/// <summary>Updates the modification time, or creates an empty file when nothing is there, like <c>touch</c>.</summary>
	public void Touch(string path, DemoAccount account)
	{
		string target = RemotePath.Normalize(path);
		lock (_gate)
		{
			(DemoNode parent, DemoNode? existing, string name) = Locate(target, account);
			if (existing is not null)
			{
				DemoNode node = existing.Kind == RemoteEntryKind.SymbolicLink ? Walk(target, account, followLast: true) : existing;
				if (!Can(node, account, Access.Write) && node.Owner != account.Name)
				{
					throw Denied(target);
				}

				node.Modified = Now;
				return;
			}

			RequireChangeable(parent, account, target);
			parent.Add(DemoNode.NewFile(name, account.Name, account.Group, DemoModes.NewFile, Now, 0, []));
			parent.Modified = Now;
		}
	}

	/// <summary>Deletes a file, link or folder. Links go themselves, never what they point at.</summary>
	public void Delete(string path, bool recursive, DemoAccount account)
	{
		string target = RemotePath.Normalize(path);
		lock (_gate)
		{
			(DemoNode parent, DemoNode? entry, _) = Locate(target, account);
			if (entry is null)
			{
				throw NotFound(target);
			}

			if (ReferenceEquals(entry, _root))
			{
				throw Denied(target);
			}

			RequireRemovable(parent, entry, account, target);
			if (entry.ChildCount > 0)
			{
				if (!recursive)
				{
					throw NotEmpty(target);
				}

				// Checked in full first, so a refused delete leaves the whole folder in place.
				RequireTreeRemovable(entry, account, target);
			}

			parent.Remove(entry);
			parent.Modified = Now;
		}
	}

	public void Rename(string sourcePath, string destinationPath, bool overwrite, DemoAccount account)
	{
		string source = RemotePath.Normalize(sourcePath);
		string destination = RemotePath.Normalize(destinationPath);
		if (source == destination)
		{
			return;
		}

		lock (_gate)
		{
			(DemoNode sourceParent, DemoNode? entry, _) = Locate(source, account);
			if (entry is null)
			{
				throw NotFound(source);
			}

			(DemoNode targetParent, DemoNode? existing, string name) = Locate(destination, account);
			if (ReferenceEquals(entry, _root) || ReferenceEquals(existing, _root))
			{
				throw Denied(source);
			}

			RequireRemovable(sourceParent, entry, account, source);
			RequireChangeable(targetParent, account, destination);
			if (entry.Kind == RemoteEntryKind.Directory && IsSameOrInside(targetParent, entry))
			{
				throw InvalidArgument(destination);
			}

			if (existing is not null)
			{
				if (ReferenceEquals(existing, entry))
				{
					return;
				}

				if (!overwrite)
				{
					throw AlreadyExists(destination);
				}

				if (existing.Kind == RemoteEntryKind.Directory && entry.Kind != RemoteEntryKind.Directory)
				{
					throw IsDirectory(destination);
				}

				if (existing.Kind != RemoteEntryKind.Directory && entry.Kind == RemoteEntryKind.Directory)
				{
					throw NotDirectory(destination);
				}

				if (existing.ChildCount > 0)
				{
					throw NotEmpty(destination);
				}

				RequireRemovable(targetParent, existing, account, destination);
				targetParent.Remove(existing);
			}

			sourceParent.Remove(entry);
			entry.Name = name;
			targetParent.Add(entry);
			sourceParent.Modified = Now;
			targetParent.Modified = Now;
		}
	}

	public void SetPermissions(string path, UnixFileMode permissions, bool recursive, DemoAccount account)
	{
		string target = RemotePath.Normalize(path);
		lock (_gate)
		{
			List<DemoNode> nodes = Collect(Walk(target, account, followLast: true), recursive, account);
			foreach (DemoNode node in nodes)
			{
				if (!account.IsRoot && node.Owner != account.Name)
				{
					throw NotPermitted(node.Path);
				}
			}

			foreach (DemoNode node in nodes)
			{
				node.Mode = permissions & PermissionBits;
			}
		}
	}

	public void SetOwner(string path, string owner, string? group, bool recursive, DemoAccount account)
	{
		string target = RemotePath.Normalize(path);
		if (!_users.Contains(owner))
		{
			throw new RemoteFileSystemException(RemoteFileErrorKind.Unknown, $"There is no user named '{owner}' on the server.", target);
		}

		if (group is not null && !_groups.Contains(group))
		{
			throw new RemoteFileSystemException(RemoteFileErrorKind.Unknown, $"There is no group named '{group}' on the server.", target);
		}

		lock (_gate)
		{
			List<DemoNode> nodes = Collect(Walk(target, account, followLast: true), recursive, account);
			foreach (DemoNode node in nodes)
			{
				// Without root, an owner can only move a file to their own group.
				bool allowed = account.IsRoot
					|| (node.Owner == account.Name && owner == account.Name && (group is null || group == account.Group));
				if (!allowed)
				{
					throw NotPermitted(node.Path);
				}
			}

			foreach (DemoNode node in nodes)
			{
				node.Owner = owner;
				node.Group = group ?? node.Group;
			}
		}
	}

	/// <summary>
	/// Creates or truncates a file for writing and returns it for <see cref="Append"/>. A new file belongs to the account
	/// with <paramref name="permissions"/> or 644; an overwritten file keeps its owner, and writing through a link writes
	/// what it points at.
	/// </summary>
	public DemoNode BeginWrite(string path, bool overwrite, UnixFileMode? permissions, DemoAccount account)
	{
		string target = RemotePath.Normalize(path);
		lock (_gate)
		{
			(DemoNode parent, DemoNode? existing, string name) = Locate(target, account);
			if (existing is not null)
			{
				if (!overwrite)
				{
					throw AlreadyExists(target);
				}

				DemoNode file = existing.Kind == RemoteEntryKind.SymbolicLink ? Walk(target, account, followLast: true) : existing;
				if (file.Kind == RemoteEntryKind.Directory)
				{
					throw IsDirectory(target);
				}

				if (!Can(file, account, Access.Write))
				{
					throw Denied(target);
				}

				file.Size = 0;
				file.Content = [];
				file.Modified = Now;
				if (permissions is { } mode && (account.IsRoot || file.Owner == account.Name))
				{
					file.Mode = mode & PermissionBits;
				}

				return file;
			}

			RequireChangeable(parent, account, target);
			DemoNode created = DemoNode.NewFile(name, account.Name, account.Group, (permissions ?? DemoModes.NewFile) & PermissionBits, Now, 0, []);
			parent.Add(created);
			parent.Modified = Now;
			return created;
		}
	}

	public void Append(DemoNode file, ReadOnlySpan<byte> data)
	{
		lock (_gate)
		{
			int stored = file.Content.Length;
			int keep = Math.Min(data.Length, MaxStoredBytes - stored);
			if (keep > 0)
			{
				byte[] content = new byte[stored + keep];
				file.Content.CopyTo(content, 0);
				data[..keep].CopyTo(content.AsSpan(stored));
				file.Content = content;
			}

			file.Size += data.Length;
			file.Modified = Now;
		}
	}

	public void CompleteWrite(DemoNode file, DateTimeOffset? lastModified)
	{
		lock (_gate)
		{
			file.Modified = lastModified ?? Now;
		}
	}

	private static bool Can(DemoNode node, DemoAccount account, Access access)
	{
		if (account.IsRoot)
		{
			return true;
		}

		int shift = node.Owner == account.Name ? 6 : node.Group == account.Group ? 3 : 0;
		return (((int)node.Mode >> shift) & (int)access) != 0;
	}

	private static void RequireChangeable(DemoNode directory, DemoAccount account, string path)
	{
		if (!Can(directory, account, Access.Write) || !Can(directory, account, Access.Search))
		{
			throw Denied(path);
		}
	}

	private static void RequireRemovable(DemoNode parent, DemoNode entry, DemoAccount account, string path)
	{
		RequireChangeable(parent, account, path);

		// In a sticky folder such as /tmp only the owner of the entry or of the folder may remove it.
		bool sticky = (parent.Mode & UnixFileMode.StickyBit) != 0;
		if (sticky && !account.IsRoot && entry.Owner != account.Name && parent.Owner != account.Name)
		{
			throw NotPermitted(path);
		}
	}

	private static void RequireTreeRemovable(DemoNode directory, DemoAccount account, string path)
	{
		if (!Can(directory, account, Access.Read))
		{
			throw Denied(path);
		}

		foreach (DemoNode child in directory.Children)
		{
			string childPath = RemotePath.Combine(path, child.Name);
			RequireRemovable(directory, child, account, childPath);
			if (child.ChildCount > 0)
			{
				RequireTreeRemovable(child, account, childPath);
			}
		}
	}

	// Recursive changes skip the links inside the tree, like chmod -R, so they never reach outside it.
	private static List<DemoNode> Collect(DemoNode start, bool recursive, DemoAccount account)
	{
		List<DemoNode> nodes = [start];
		for (int i = 0; recursive && i < nodes.Count; i++)
		{
			DemoNode node = nodes[i];
			if (node.Kind != RemoteEntryKind.Directory)
			{
				continue;
			}

			if (!Can(node, account, Access.Read) || !Can(node, account, Access.Search))
			{
				throw Denied(node.Path);
			}

			nodes.AddRange(node.Children.Where(child => child.Kind != RemoteEntryKind.SymbolicLink));
		}

		return nodes;
	}

	private static bool IsSameOrInside(DemoNode node, DemoNode ancestor)
	{
		for (DemoNode? current = node; current is not null; current = current.Parent)
		{
			if (ReferenceEquals(current, ancestor))
			{
				return true;
			}
		}

		return false;
	}

	private static void PushParts(Stack<string> pending, string path)
	{
		string[] parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
		for (int i = parts.Length - 1; i >= 0; i--)
		{
			pending.Push(parts[i]);
		}
	}

	private static RemoteFileSystemException ErrorFor(Failure failure, string path) => failure switch
	{
		Failure.NotFound => NotFound(path),
		Failure.NotDirectory => NotDirectory(path),
		Failure.Denied => Denied(path),
		_ => new RemoteFileSystemException(RemoteFileErrorKind.Unknown, "Too many levels of symbolic links", path),
	};

	private static RemoteFileSystemException NotFound(string path) =>
		new(RemoteFileErrorKind.NotFound, "No such file or directory", path);

	private static RemoteFileSystemException Denied(string path) =>
		new(RemoteFileErrorKind.PermissionDenied, "Permission denied", path);

	private static RemoteFileSystemException NotPermitted(string path) =>
		new(RemoteFileErrorKind.PermissionDenied, "Operation not permitted", path);

	private static RemoteFileSystemException AlreadyExists(string path) =>
		new(RemoteFileErrorKind.AlreadyExists, "File exists", path);

	private static RemoteFileSystemException NotEmpty(string path) =>
		new(RemoteFileErrorKind.DirectoryNotEmpty, "Directory not empty", path);

	private static RemoteFileSystemException NotDirectory(string path) =>
		new(RemoteFileErrorKind.Unknown, "Not a directory", path);

	private static RemoteFileSystemException IsDirectory(string path) =>
		new(RemoteFileErrorKind.Unknown, "Is a directory", path);

	private static RemoteFileSystemException InvalidArgument(string path) =>
		new(RemoteFileErrorKind.Unknown, "Invalid argument", path);

	/// <summary>The folder that holds <paramref name="target"/> and the entry itself, without following a link there.</summary>
	private (DemoNode Parent, DemoNode? Entry, string Name) Locate(string target, DemoAccount account)
	{
		if (target == RemotePath.Root)
		{
			return (_root, _root, "");
		}

		DemoNode parent = Walk(RemotePath.GetParent(target), account, followLast: true);
		if (parent.Kind != RemoteEntryKind.Directory)
		{
			throw NotDirectory(target);
		}

		if (!Can(parent, account, Access.Search))
		{
			throw Denied(target);
		}

		string name = RemotePath.GetName(target);
		return (parent, parent.Child(name), name);
	}

	private DemoNode Walk(string path, DemoAccount account, bool followLast) =>
		TryWalk(path, account, followLast, out Failure failure) ?? throw ErrorFor(failure, path);

	/// <summary>
	/// Walks <paramref name="path"/> from the root, checking search permission on every folder it passes. Links on the way
	/// are followed, and the last component too when <paramref name="followLast"/> is set.
	/// </summary>
	private DemoNode? TryWalk(string path, DemoAccount account, bool followLast, out Failure failure)
	{
		failure = Failure.None;
		Stack<string> pending = new();
		PushParts(pending, path);
		DemoNode current = _root;
		int hops = 0;
		while (pending.TryPop(out string? part))
		{
			if (current.Kind != RemoteEntryKind.Directory)
			{
				failure = Failure.NotDirectory;
				return null;
			}

			if (!Can(current, account, Access.Search))
			{
				failure = Failure.Denied;
				return null;
			}

			if (part == ".")
			{
				continue;
			}

			if (part == "..")
			{
				current = current.Parent ?? current;
				continue;
			}

			DemoNode? child = current.Child(part);
			if (child is null)
			{
				failure = Failure.NotFound;
				return null;
			}

			if (child.Kind == RemoteEntryKind.SymbolicLink && (pending.Count > 0 || followLast))
			{
				if (++hops > MaxLinkHops)
				{
					failure = Failure.Loop;
					return null;
				}

				// A relative target continues from the folder holding the link; an absolute one from the root.
				string linkTarget = child.LinkTarget ?? "";
				PushParts(pending, linkTarget);
				current = linkTarget.StartsWith('/') ? _root : current;
				continue;
			}

			current = child;
		}

		return current;
	}

	private RemoteFileEntry ToEntry(DemoNode node, string path, DemoAccount account) => new()
	{
		Name = RemotePath.GetName(path),
		Path = path,
		Kind = node.Kind,
		Size = node.Size,
		LastModified = node.Modified,
		Permissions = node.Mode,
		Owner = node.Owner,
		Group = node.Group,
		LinkTarget = node.LinkTarget,
		LinkTargetKind = node.Kind == RemoteEntryKind.SymbolicLink ? LinkTargetKindOf(node, account) : null,
	};

	private RemoteEntryKind? LinkTargetKindOf(DemoNode link, DemoAccount account)
	{
		string target = link.LinkTarget ?? "";
		string absolute = target.StartsWith('/') ? target : RemotePath.Combine(link.Parent?.Path ?? RemotePath.Root, target);
		return TryWalk(absolute, account, followLast: true, out _)?.Kind;
	}
}
