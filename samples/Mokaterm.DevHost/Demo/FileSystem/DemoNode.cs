using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.DevHost.Demo.FileSystem;

/// <summary>A folder, file or symbolic link in a <see cref="DemoFileTree"/>. Only the tree changes nodes, under its lock.</summary>
internal sealed class DemoNode
{
	private const long DirectorySize = 4096;

	private readonly Dictionary<string, DemoNode>? _children;

	private DemoNode(RemoteEntryKind kind, string name, string owner, string group, UnixFileMode mode, DateTimeOffset modified)
	{
		Kind = kind;
		Name = name;
		Owner = owner;
		Group = group;
		Mode = mode;
		Modified = modified;
		if (kind == RemoteEntryKind.Directory)
		{
			_children = new Dictionary<string, DemoNode>(StringComparer.Ordinal);
			Size = DirectorySize;
		}
	}

	public RemoteEntryKind Kind { get; }

	public string Name { get; set; }

	public DemoNode? Parent { get; private set; }

	public string Owner { get; set; }

	public string Group { get; set; }

	public UnixFileMode Mode { get; set; }

	public long Size { get; set; }

	public DateTimeOffset Modified { get; set; }

	public string? LinkTarget { get; private init; }

	/// <summary>The stored bytes: the whole file when it is small, otherwise only its beginning.</summary>
	public byte[] Content { get; set; } = [];

	public IReadOnlyCollection<DemoNode> Children => _children is null ? [] : _children.Values;

	public int ChildCount => _children?.Count ?? 0;

	/// <summary>The canonical path, with no links in it.</summary>
	public string Path => Parent is null ? RemotePath.Root : RemotePath.Combine(Parent.Path, Name);

	public static DemoNode NewDirectory(string name, string owner, string group, UnixFileMode mode, DateTimeOffset modified) =>
		new(RemoteEntryKind.Directory, name, owner, group, mode, modified);

	public static DemoNode NewFile(string name, string owner, string group, UnixFileMode mode, DateTimeOffset modified, long size, byte[] content) =>
		new(RemoteEntryKind.File, name, owner, group, mode, modified) { Size = size, Content = content };

	public static DemoNode NewLink(string name, string target, string owner, string group, DateTimeOffset modified) =>
		new(RemoteEntryKind.SymbolicLink, name, owner, group, DemoModes.Link, modified) { LinkTarget = target, Size = target.Length };

	public DemoNode? Child(string name) => _children?.GetValueOrDefault(name);

	/// <summary>Adds <paramref name="child"/>, replacing an entry with the same name. Returns the child for chaining.</summary>
	public DemoNode Add(DemoNode child)
	{
		if (_children is null)
		{
			throw new InvalidOperationException("Only folders hold entries.");
		}

		child.Parent = this;
		_children[child.Name] = child;
		return child;
	}

	public void Remove(DemoNode child)
	{
		if (_children is not null && _children.Remove(child.Name))
		{
			child.Parent = null;
		}
	}
}
