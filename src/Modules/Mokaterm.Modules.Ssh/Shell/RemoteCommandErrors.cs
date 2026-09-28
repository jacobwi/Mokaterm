using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Modules.Ssh.Elevation;

namespace Mokaterm.Modules.Ssh.Shell;

/// <summary>Maps the exit status and stderr of <see cref="RemoteScripts"/> (C locale) to <see cref="RemoteFileSystemException"/>.</summary>
internal static class RemoteCommandErrors
{
	public static RemoteFileSystemException ToException(int? exitStatus, string error, string path, string? destination = null) => exitStatus switch
	{
		RemoteScripts.ExitNotFound => new RemoteFileSystemException(RemoteFileErrorKind.NotFound, $"{path} does not exist.", path),
		RemoteScripts.ExitAlreadyExists => new RemoteFileSystemException(RemoteFileErrorKind.AlreadyExists, $"{destination ?? path} already exists.", destination ?? path),
		RemoteScripts.ExitDirectoryNotEmpty => new RemoteFileSystemException(RemoteFileErrorKind.DirectoryNotEmpty, $"{destination ?? path} is a directory that is not empty.", destination ?? path),
		RemoteScripts.ExitWrongKind => new RemoteFileSystemException(RemoteFileErrorKind.Unknown, $"{destination ?? path} is a directory.", destination ?? path),
		_ => FromMessage(error, path),
	};

	private static RemoteFileSystemException FromMessage(string error, string path)
	{
		string line = SudoErrors.FirstLine(error);
		RemoteFileErrorKind kind =
			Has(line, "No such file or directory") || Has(line, "Not a directory") ? RemoteFileErrorKind.NotFound
			: Has(line, "Permission denied") || Has(line, "Operation not permitted") || Has(line, "Read-only file system") ? RemoteFileErrorKind.PermissionDenied
			: Has(line, "File exists") ? RemoteFileErrorKind.AlreadyExists
			: Has(line, "Directory not empty") ? RemoteFileErrorKind.DirectoryNotEmpty
			: RemoteFileErrorKind.Unknown;

		string message = line.Length > 0 ? line : $"The command for {path} failed.";
		return new RemoteFileSystemException(kind, message, path);
	}

	private static bool Has(string text, string value) => text.Contains(value, StringComparison.OrdinalIgnoreCase);
}
