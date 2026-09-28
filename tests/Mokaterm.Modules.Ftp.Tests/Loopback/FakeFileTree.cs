using System.Text;
using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.Modules.Ftp.Tests.Loopback;

internal enum FakeNodeKind
{
	File,
	Directory,
	Link,
}

internal sealed class FakeNode
{
	public required FakeNodeKind Kind { get; init; }

	public byte[] Content { get; set; } = [];

	public UnixFileMode Mode { get; set; }

	public DateTime Modified { get; set; } = FakeFileTree.DefaultModified;

	public string? LinkTarget { get; init; }
}

/// <summary>The loopback server's file system, keyed by absolute path.</summary>
internal sealed class FakeFileTree
{
	public static readonly DateTime DefaultModified = new(2025, 5, 6, 7, 8, 9, DateTimeKind.Utc);

	private readonly Lock _gate = new();
	private readonly Dictionary<string, FakeNode> _nodes = new(StringComparer.Ordinal)
	{
		[RemotePath.Root] = new FakeNode { Kind = FakeNodeKind.Directory, Mode = Mode("755") },
	};

	public static UnixFileMode Mode(string octal) => UnixFileModeFormat.TryParseOctal(octal, out UnixFileMode mode) ? mode : throw new ArgumentException(octal);

	public FakeFileTree AddDirectory(string path, string mode = "755")
	{
		lock (_gate)
		{
			EnsureParents(path);
			_nodes[RemotePath.Normalize(path)] = new FakeNode { Kind = FakeNodeKind.Directory, Mode = Mode(mode) };
		}

		return this;
	}

	public FakeFileTree AddFile(string path, byte[] content, string mode = "644")
	{
		lock (_gate)
		{
			EnsureParents(path);
			_nodes[RemotePath.Normalize(path)] = new FakeNode { Kind = FakeNodeKind.File, Content = content, Mode = Mode(mode) };
		}

		return this;
	}

	public FakeFileTree AddFile(string path, string text, string mode = "644") => AddFile(path, Encoding.UTF8.GetBytes(text), mode);

	public FakeFileTree AddLink(string path, string target)
	{
		lock (_gate)
		{
			EnsureParents(path);
			_nodes[RemotePath.Normalize(path)] = new FakeNode { Kind = FakeNodeKind.Link, LinkTarget = target, Mode = Mode("777") };
		}

		return this;
	}

	public FakeNode? Get(string path)
	{
		lock (_gate)
		{
			return _nodes.GetValueOrDefault(RemotePath.Normalize(path));
		}
	}

	/// <summary>The node, following links.</summary>
	public FakeNode? Resolve(string path)
	{
		lock (_gate)
		{
			string current = RemotePath.Normalize(path);
			FakeNode? node = _nodes.GetValueOrDefault(current);
			for (int hops = 0; node is { Kind: FakeNodeKind.Link } && hops < 8; hops++)
			{
				current = RemotePath.Combine(RemotePath.GetParent(current), node.LinkTarget ?? "");
				node = _nodes.GetValueOrDefault(current);
			}

			return node;
		}
	}

	public bool IsDirectory(string path) => Resolve(path)?.Kind == FakeNodeKind.Directory;

	public IReadOnlyList<(string Name, FakeNode Node)> Children(string directory)
	{
		string parent = RemotePath.Normalize(directory);
		lock (_gate)
		{
			return [.. _nodes
				.Where(pair => pair.Key != RemotePath.Root && RemotePath.GetParent(pair.Key) == parent)
				.OrderBy(pair => pair.Key, StringComparer.Ordinal)
				.Select(pair => (RemotePath.GetName(pair.Key), pair.Value))];
		}
	}

	public bool Remove(string path)
	{
		lock (_gate)
		{
			return _nodes.Remove(RemotePath.Normalize(path));
		}
	}

	public void Move(string source, string destination)
	{
		string from = RemotePath.Normalize(source);
		string to = RemotePath.Normalize(destination);
		lock (_gate)
		{
			List<string> moved = [.. _nodes.Keys.Where(key => RemotePath.IsSameOrInside(key, from))];
			foreach (string key in moved)
			{
				FakeNode node = _nodes[key];
				_nodes.Remove(key);
				_nodes[to + key[from.Length..]] = node;
			}
		}
	}

	public void Write(string path, byte[] content)
	{
		lock (_gate)
		{
			string key = RemotePath.Normalize(path);
			if (_nodes.TryGetValue(key, out FakeNode? existing) && existing.Kind == FakeNodeKind.File)
			{
				existing.Content = content;
				existing.Modified = DateTime.UtcNow;
				return;
			}

			_nodes[key] = new FakeNode { Kind = FakeNodeKind.File, Content = content, Mode = Mode("644"), Modified = DateTime.UtcNow };
		}
	}

	private void EnsureParents(string path)
	{
		for (string parent = RemotePath.GetParent(path); !RemotePath.IsRoot(parent); parent = RemotePath.GetParent(parent))
		{
			if (!_nodes.ContainsKey(parent))
			{
				_nodes[parent] = new FakeNode { Kind = FakeNodeKind.Directory, Mode = Mode("755") };
			}
		}
	}
}
