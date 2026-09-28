namespace Mokaterm.UI.FileBrowser.Browsing;

/// <summary>Builds shell input from remote paths and names, which may contain anything but '/' and NUL.</summary>
internal static class PosixShell
{
	/// <summary>Wraps <paramref name="value"/> in single quotes for a POSIX shell. Embedded quotes become <c>'\''</c>.</summary>
	public static string Quote(string value) => "'" + value.Replace("'", @"'\''", StringComparison.Ordinal) + "'";

	/// <summary>
	/// The line that makes an interactive shell change into <paramref name="directory"/>, Enter included, or null when the
	/// path holds a control character. The line is typed, not run: quoting cannot stop a Ctrl+C or Ctrl+U in a folder name
	/// from ending the line early, and whatever follows it would then run as a command.
	/// </summary>
	public static string? ChangeDirectoryCommand(string directory) =>
		directory.Any(char.IsControl) ? null : "cd -- " + Quote(directory) + "\r";
}
