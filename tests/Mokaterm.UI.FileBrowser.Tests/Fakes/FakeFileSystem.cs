using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.UI.FileBrowser.Tests.Fakes;

/// <summary>An in-memory tree that records the changes asked of it as short command lines.</summary>
internal sealed class FakeFileSystem : IRemoteFileSystem
{
	private readonly Dictionary<string, List<RemoteFileEntry>> _folders = new(StringComparer.Ordinal);

	public RemoteFileSystemFeatures Features { get; set; } = RemoteFileSystemFeatures.Permissions | RemoteFileSystemFeatures.Ownership;

	public string UserName => "abc";

	public bool IsElevated => Elevated;

	/// <summary>Stands in for a root view.</summary>
	public bool Elevated { get; init; }

	/// <summary>Folders whose listing is refused.</summary>
	public HashSet<string> Denied { get; } = new(StringComparer.Ordinal);

	public List<string> Calls { get; } = [];

	/// <summary>Runs before every listing, for holding a walk at a folder.</summary>
	public Func<string, CancellationToken, Task>? BeforeList { get; set; }

	/// <summary>Failures for the next uploads, in order; an upload with none left succeeds.</summary>
	public Queue<Exception> UploadFailures { get; } = new();

	/// <summary>Links to folders, keyed by the link's own path; resolving and stat follow them.</summary>
	public Dictionary<string, string> FolderLinks { get; } = new(StringComparer.Ordinal);

	public static RemoteFileEntry File(string path, long size = 0, UnixFileMode mode = (UnixFileMode)0x1A4, string owner = "abc", string group = "abc") => new()
	{
		Name = RemotePath.GetName(path),
		Path = path,
		Kind = RemoteEntryKind.File,
		Size = size,
		Permissions = mode,
		Owner = owner,
		Group = group,
	};

	public static RemoteFileEntry Folder(string path, UnixFileMode mode = (UnixFileMode)0x1ED, string owner = "abc", string group = "abc") => new()
	{
		Name = RemotePath.GetName(path),
		Path = path,
		Kind = RemoteEntryKind.Directory,
		Permissions = mode,
		Owner = owner,
		Group = group,
	};

	public static RemoteFileEntry Link(string path, RemoteEntryKind? targetKind = RemoteEntryKind.File) => new()
	{
		Name = RemotePath.GetName(path),
		Path = path,
		Kind = RemoteEntryKind.SymbolicLink,
		Size = 12,
		Permissions = (UnixFileMode)0x1FF,
		Owner = "abc",
		Group = "abc",
		LinkTarget = "/elsewhere",
		LinkTargetKind = targetKind,
	};

	public FakeFileSystem Contains(string folder, params RemoteFileEntry[] children)
	{
		_folders[folder] = [.. children];
		return this;
	}

	public ValueTask<string> GetHomeDirectoryAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult("/home/abc");

	public ValueTask<string> ResolvePathAsync(string path, CancellationToken cancellationToken = default) => ValueTask.FromResult(Resolve(path));

	public async ValueTask<IReadOnlyList<RemoteFileEntry>> ListAsync(string path, CancellationToken cancellationToken = default)
	{
		Calls.Add("list " + path);
		if (BeforeList is not null)
		{
			await BeforeList(path, cancellationToken);
		}

		cancellationToken.ThrowIfCancellationRequested();
		if (Denied.Contains(path))
		{
			throw new RemoteFileSystemException(RemoteFileErrorKind.PermissionDenied, "Permission denied", path);
		}

		return _folders.TryGetValue(path, out List<RemoteFileEntry>? children)
			? children
			: throw new RemoteFileSystemException(RemoteFileErrorKind.NotFound, "No such folder", path);
	}

	public ValueTask<RemoteFileEntry?> StatAsync(string path, CancellationToken cancellationToken = default) => ValueTask.FromResult(Find(path));

	public ValueTask SetPermissionsAsync(string path, UnixFileMode permissions, bool recursive, CancellationToken cancellationToken = default)
	{
		Calls.Add($"chmod{Recursive(recursive)} {UnixFileModeFormat.ToOctal(permissions)} {path}");
		return ValueTask.CompletedTask;
	}

	public ValueTask ChangePermissionsAsync(string path, PermissionChange change, CancellationToken cancellationToken = default)
	{
		string mask = UnixFileModeFormat.ToOctal(change.Mask);
		string extras = (change.Targets == PermissionTargets.All ? "" : " " + change.Targets.ToString().ToLowerInvariant())
			+ (change.ConditionalExecute ? " X" : "");
		Calls.Add($"chmod{Recursive(change.Recursive)} {UnixFileModeFormat.ToOctal(change.Mode)}/{mask}{extras} {path}");
		return ValueTask.CompletedTask;
	}

	public ValueTask SetOwnerAsync(string path, string owner, string? group, bool recursive, CancellationToken cancellationToken = default)
	{
		Calls.Add($"chown{Recursive(recursive)} {owner}{(group is null ? "" : ":" + group)} {path}");
		return ValueTask.CompletedTask;
	}

	public ValueTask SetGroupAsync(string path, string group, bool recursive, CancellationToken cancellationToken = default)
	{
		Calls.Add($"chgrp{Recursive(recursive)} {group} {path}");
		return ValueTask.CompletedTask;
	}

	public ValueTask CreateDirectoryAsync(string path, CancellationToken cancellationToken = default)
	{
		Calls.Add("mkdir " + path);
		return ValueTask.CompletedTask;
	}

	public ValueTask DeleteAsync(string path, bool recursive, CancellationToken cancellationToken = default) => throw new NotSupportedException();

	/// <summary>Records the move and, like a server, refuses to replace an existing entry unless told to.</summary>
	public ValueTask RenameAsync(string sourcePath, string destinationPath, bool overwrite, CancellationToken cancellationToken = default)
	{
		Calls.Add($"mv{(overwrite ? " -f" : "")} {sourcePath} {destinationPath}");
		return !overwrite && Find(destinationPath) is not null
			? ValueTask.FromException(new RemoteFileSystemException(RemoteFileErrorKind.AlreadyExists, "Already exists", destinationPath))
			: ValueTask.CompletedTask;
	}

	public ValueTask<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();

	public ValueTask UploadAsync(string path, Stream source, UploadOptions options, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
	{
		Calls.Add($"put{(options.Overwrite ? " -f" : "")} {path}");
		return UploadFailures.TryDequeue(out Exception? failure) ? ValueTask.FromException(failure) : ValueTask.CompletedTask;
	}

	public ValueTask DownloadAsync(string path, Stream destination, IProgress<long>? progress = null, CancellationToken cancellationToken = default) =>
		throw new NotSupportedException();

	public ValueTask DisposeAsync() => ValueTask.CompletedTask;

	public static UnixFileMode Mode(string octal) => (UnixFileMode)Convert.ToInt32(octal, 8);

	private static string Recursive(bool recursive) => recursive ? " -R" : "";

	private RemoteFileEntry? Find(string path)
	{
		string resolved = Resolve(path);
		return _folders.Values.SelectMany(children => children).FirstOrDefault(entry => entry.Path == resolved);
	}

	// Follows the links in FolderLinks the way realpath does, for paths that go through one.
	private string Resolve(string path)
	{
		string resolved = RemotePath.Normalize(path);
		foreach ((string link, string target) in FolderLinks)
		{
			if (resolved == link || resolved.StartsWith(link + "/", StringComparison.Ordinal))
			{
				resolved = RemotePath.Normalize(target + resolved[link.Length..]);
			}
		}

		return resolved;
	}
}
