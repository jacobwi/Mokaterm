using Mokaterm.Abstractions.Platform;

namespace Mokaterm.Maui.Services;

/// <summary>Turns local file system paths into <see cref="LocalFileItem"/> lists for uploads.</summary>
internal static class LocalPaths
{
	private const int ReadBufferSize = 128 * 1024;

	/// <summary>
	/// Files become single items; directories become the directory followed by everything inside it, parents before
	/// children, with relative paths rooted at the directory's own name.
	/// </summary>
	public static IReadOnlyList<LocalFileItem> Expand(IEnumerable<string> paths)
	{
		List<LocalFileItem> items = [];
		foreach (string path in paths)
		{
			if (Directory.Exists(path))
			{
				AddDirectory(items, new DirectoryInfo(path));
			}
			else if (File.Exists(path))
			{
				FileInfo file = new(path);
				items.Add(ForFile(file, file.Name));
			}
		}

		return items;
	}

	public static LocalFileItem ForFile(FileInfo file, string relativePath) => new()
	{
		Name = file.Name,
		RelativePath = relativePath,
		Length = file.Length,
		LastModified = file.LastWriteTimeUtc,
		OpenReadAsync = _ => ValueTask.FromResult<Stream>(new FileStream(
			file.FullName,
			FileMode.Open,
			FileAccess.Read,
			FileShare.Read,
			ReadBufferSize,
			FileOptions.Asynchronous | FileOptions.SequentialScan)),
	};

	private static void AddDirectory(List<LocalFileItem> items, DirectoryInfo root)
	{
		items.Add(new LocalFileItem { Name = root.Name, RelativePath = root.Name, IsDirectory = true, LastModified = root.LastWriteTimeUtc });

		EnumerationOptions options = new()
		{
			RecurseSubdirectories = true,
			IgnoreInaccessible = true,
			AttributesToSkip = FileAttributes.ReparsePoint,
		};

		// Directories first so every parent exists remotely before files are uploaded into it.
		foreach (DirectoryInfo directory in root.EnumerateDirectories("*", options).OrderBy(d => d.FullName.Length))
		{
			items.Add(new LocalFileItem
			{
				Name = directory.Name,
				RelativePath = Relative(root, directory.FullName),
				IsDirectory = true,
				LastModified = directory.LastWriteTimeUtc,
			});
		}

		foreach (FileInfo file in root.EnumerateFiles("*", options))
		{
			items.Add(ForFile(file, Relative(root, file.FullName)));
		}
	}

	private static string Relative(DirectoryInfo root, string fullPath) =>
		root.Name + "/" + Path.GetRelativePath(root.FullName, fullPath).Replace(Path.DirectorySeparatorChar, '/');
}
