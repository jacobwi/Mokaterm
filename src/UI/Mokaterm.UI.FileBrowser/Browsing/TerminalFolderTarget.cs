using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.UI.FileBrowser.Browsing;

/// <summary>Places a folder the session's terminal reported on the server, for a browser that follows it.</summary>
internal static class TerminalFolderTarget
{
	/// <summary>Where <c>~</c> leads for root, which is the same on every system a prompt is likely to come from.</summary>
	public const string RootHome = "/root";

	/// <summary>
	/// The absolute path <paramref name="folder"/> names, or null when it cannot be placed. An absolute path is taken as it
	/// is. <c>~</c> is the home of the prompt's <paramref name="user"/>: <paramref name="home"/> when that is the login
	/// (or the report names nobody), <see cref="RootHome"/> after a <c>sudo -i</c>, and unknown for anyone else. A bare
	/// name, the last part of the folder that some prompts show, says nothing about where it is.
	/// </summary>
	public static string? Resolve(string folder, string? user, string loginUser, string home)
	{
		ArgumentNullException.ThrowIfNull(folder);
		if (folder.Length == 0 || folder.Any(char.IsControl))
		{
			return null;
		}

		if (folder[0] == RemotePath.Separator)
		{
			return RemotePath.Normalize(folder);
		}

		// "~alice/src" is another user's home, which the browser has no way to find.
		if (folder[0] != '~' || (folder.Length > 1 && folder[1] != RemotePath.Separator))
		{
			return null;
		}

		string? baseFolder = user is null || user == loginUser ? home
			: user == "root" ? RootHome
			: null;
		return baseFolder is null ? null : RemotePath.Normalize(baseFolder + folder[1..]);
	}
}
