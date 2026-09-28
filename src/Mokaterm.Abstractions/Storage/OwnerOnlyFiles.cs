namespace Mokaterm.Abstractions.Storage;

/// <summary>
/// The Unix modes for files nobody but their owner may read, and for the directories that hold them. The vault header
/// and its documents are what an offline password guess starts from, and a session log is a whole shell transcript, so
/// the default umask leaving either readable by every account on the machine is what this exists to prevent. Windows
/// needs nothing: a file there inherits the directory's ACL, which is the user's own profile on desktop.
/// </summary>
public static class OwnerOnlyFiles
{
	/// <summary>0600.</summary>
	public const UnixFileMode ForFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;

	/// <summary>0700: the execute bit is what lets the owner enter the directory.</summary>
	public const UnixFileMode ForDirectory = ForFile | UnixFileMode.UserExecute;

	/// <summary>
	/// Creates <paramref name="path"/> and any missing parents, owner-only where that means anything. A directory that
	/// already exists keeps the mode it has.
	/// </summary>
	public static void CreateDirectory(string path)
	{
		if (OperatingSystem.IsWindows())
		{
			Directory.CreateDirectory(path);
		}
		else
		{
			Directory.CreateDirectory(path, ForDirectory);
		}
	}

	/// <summary>
	/// <paramref name="options"/> with <see cref="FileStreamOptions.UnixCreateMode"/> set where the platform has one, so
	/// a file the stream creates is owner-only from the moment it appears rather than after a chmod.
	/// </summary>
	public static FileStreamOptions Owned(FileStreamOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		if (!OperatingSystem.IsWindows())
		{
			options.UnixCreateMode = ForFile;
		}

		return options;
	}
}
