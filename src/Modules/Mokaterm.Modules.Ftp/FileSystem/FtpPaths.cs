using System.Text;
using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.Modules.Ftp.FileSystem;

/// <summary>Path handling FTP needs on top of <see cref="RemotePath"/>.</summary>
internal static class FtpPaths
{
	/// <summary>
	/// Makes <paramref name="path"/> absolute: <c>~</c> and <c>~/x</c> start at <paramref name="homeDirectory"/>, relative
	/// paths too, and <c>.</c> and <c>..</c> are resolved lexically.
	/// </summary>
	public static string Resolve(string? path, string homeDirectory)
	{
		if (string.IsNullOrEmpty(path) || path == "~")
		{
			return RemotePath.Normalize(homeDirectory);
		}

		if (path.StartsWith("~/", StringComparison.Ordinal))
		{
			return RemotePath.Combine(homeDirectory, path[2..]);
		}

		return path[0] == RemotePath.Separator ? RemotePath.Normalize(path) : RemotePath.Combine(homeDirectory, path);
	}

	/// <summary>The directory FluentFTP read after login, or <c>/</c> when the server reported something that is not a POSIX path.</summary>
	public static string ReadLoginDirectory(string? workingDirectory) =>
		!string.IsNullOrEmpty(workingDirectory) && workingDirectory[0] == RemotePath.Separator
			? RemotePath.Normalize(workingDirectory)
			: RemotePath.Root;

	/// <summary>
	/// Reads the quoted path from a PWD or MKD reply text such as <c>"/home/abc" is the current directory</c>. Doubled
	/// quotes inside the path stand for one quote (RFC 959).
	/// </summary>
	public static string? ParseQuotedPath(string? message)
	{
		if (string.IsNullOrEmpty(message))
		{
			return null;
		}

		int start = message.IndexOf('"');
		if (start < 0)
		{
			return null;
		}

		StringBuilder path = new();
		for (int i = start + 1; i < message.Length; i++)
		{
			if (message[i] != '"')
			{
				path.Append(message[i]);
				continue;
			}

			if (i + 1 < message.Length && message[i + 1] == '"')
			{
				path.Append('"');
				i++;
				continue;
			}

			return path.ToString();
		}

		return null;
	}

	/// <summary>False for paths with line breaks or NUL, which would split a raw FTP command in two.</summary>
	public static bool IsSendable(string path) => path.AsSpan().IndexOfAny('\r', '\n', '\0') < 0;
}
