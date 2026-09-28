namespace Mokaterm.Abstractions.FileSystem;

/// <summary>One step in a path, for breadcrumbs: <c>("etc", "/etc")</c>.</summary>
public readonly record struct RemotePathSegment(string Name, string Path);

/// <summary>POSIX path helpers. Remote paths always use '/', whatever the local OS uses.</summary>
public static class RemotePath
{
	public const char Separator = '/';

	public const string Root = "/";

	public static bool IsRoot(string path) => Normalize(path) == Root;

	/// <summary>Joins <paramref name="name"/> onto <paramref name="directory"/>. An absolute <paramref name="name"/> wins.</summary>
	public static string Combine(string directory, string name)
	{
		if (string.IsNullOrEmpty(name))
		{
			return Normalize(directory);
		}

		if (name[0] == Separator)
		{
			return Normalize(name);
		}

		return Normalize(directory.EndsWith(Separator) ? directory + name : directory + Separator + name);
	}

	/// <summary>The containing directory. The parent of <c>/</c> is <c>/</c>.</summary>
	public static string GetParent(string path)
	{
		string normalized = Normalize(path);
		int index = normalized.LastIndexOf(Separator);
		return index <= 0 ? Root : normalized[..index];
	}

	/// <summary>The last segment, or empty for <c>/</c>.</summary>
	public static string GetName(string path)
	{
		string normalized = Normalize(path);
		return normalized == Root ? "" : normalized[(normalized.LastIndexOf(Separator) + 1)..];
	}

	/// <summary>
	/// Collapses repeated slashes and resolves <c>.</c> and <c>..</c> lexically. Relative paths are treated as
	/// relative to <c>/</c>; resolve <c>~</c> with <see cref="IRemoteFileSystem.ResolvePathAsync"/> first.
	/// </summary>
	public static string Normalize(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return Root;
		}

		Stack<string> parts = new();
		foreach (string part in path.Split(Separator, StringSplitOptions.RemoveEmptyEntries))
		{
			if (part == ".")
			{
				continue;
			}

			if (part == "..")
			{
				parts.TryPop(out _);
				continue;
			}

			parts.Push(part);
		}

		return parts.Count == 0 ? Root : Separator + string.Join(Separator, parts.Reverse());
	}

	/// <summary>Breadcrumb segments starting with the root: <c>/</c>, <c>/etc</c>, <c>/etc/nginx</c>.</summary>
	public static IReadOnlyList<RemotePathSegment> GetSegments(string path)
	{
		string normalized = Normalize(path);
		List<RemotePathSegment> segments = [new(Root, Root)];
		if (normalized == Root)
		{
			return segments;
		}

		string current = "";
		foreach (string part in normalized.Split(Separator, StringSplitOptions.RemoveEmptyEntries))
		{
			current += Separator + part;
			segments.Add(new RemotePathSegment(part, current));
		}

		return segments;
	}

	/// <summary>True for a usable single file name: not empty, not <c>.</c> or <c>..</c>, no '/' or NUL.</summary>
	public static bool IsValidName(string? name) =>
		!string.IsNullOrEmpty(name) && name is not ("." or "..") && name.IndexOfAny([Separator, '\0']) < 0;

	/// <summary>True when <paramref name="path"/> is <paramref name="ancestor"/> or lies inside it.</summary>
	public static bool IsSameOrInside(string path, string ancestor)
	{
		string normalizedPath = Normalize(path);
		string normalizedAncestor = Normalize(ancestor);
		return normalizedAncestor == Root
			|| normalizedPath == normalizedAncestor
			|| normalizedPath.StartsWith(normalizedAncestor + Separator, StringComparison.Ordinal);
	}
}
