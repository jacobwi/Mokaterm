using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.Modules.Ssh.Shell;

/// <summary>
/// The positional arguments of the copy and archive scripts in <see cref="RemoteScripts"/>, built in one place for the
/// SFTP view, which runs them on the session's SSH connection, and the root view, which runs them through sudo.
/// </summary>
internal static class RemoteScriptArguments
{
	/// <summary><see cref="RemoteScripts.Copy"/>: source, destination, and <c>1</c> to replace.</summary>
	public static string[] Copy(string source, string destination, bool overwrite) => [source, destination, overwrite ? "1" : ""];

	/// <summary>
	/// <see cref="RemoteScripts.Compress"/>: the folder, the archive, then each entry as <c>./name</c>, so a name that
	/// starts with a dash is still a name to tar. A name that is not one entry of the folder is refused rather than sent.
	/// </summary>
	public static string[] Compress(string directory, IReadOnlyList<string> names, string archive)
	{
		ArgumentNullException.ThrowIfNull(names);
		if (names.Count == 0)
		{
			throw new RemoteFileSystemException(RemoteFileErrorKind.Unknown, "There is nothing to put in the archive.", archive);
		}

		List<string> arguments = [directory, archive];
		foreach (string name in names)
		{
			if (!RemotePath.IsValidName(name))
			{
				throw new RemoteFileSystemException(RemoteFileErrorKind.Unknown, $"'{name}' is not the name of an entry in {directory}.", directory);
			}

			arguments.Add("./" + name);
		}

		return [.. arguments];
	}

	/// <summary><see cref="RemoteScripts.Extract"/>: the archive, the new folder, and the kind its name says it is.</summary>
	public static string[] Extract(string archive, string destination)
	{
		string kind = RemoteArchives.KindOf(RemotePath.GetName(archive)) switch
		{
			RemoteArchiveKind.Tar => "tar",
			RemoteArchiveKind.TarGzip => "gz",
			RemoteArchiveKind.TarBzip2 => "bz2",
			RemoteArchiveKind.TarXz => "xz",
			RemoteArchiveKind.Zip => "zip",
			_ => throw new RemoteFileSystemException(RemoteFileErrorKind.NotSupported, $"{RemotePath.GetName(archive)} is not an archive this can unpack.", archive),
		};
		return [archive, destination, kind];
	}
}
