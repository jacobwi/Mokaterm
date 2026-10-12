using System.Text;

namespace Mokaterm.Abstractions.Commands;

/// <summary>
/// The one way anything here puts an untrusted word on a POSIX shell command line: remote paths and names, which may
/// contain anything but NUL, and values a user typed.
/// </summary>
public static class PosixShell
{
	/// <summary>
	/// Quotes <paramref name="value"/> as one shell word. Nothing is special inside single quotes, so spaces, newlines,
	/// <c>$</c>, backticks and non-ASCII text pass through as is; an embedded quote is written as <c>'\''</c>.
	/// </summary>
	/// <exception cref="ArgumentException">The value contains NUL, which no command line can carry.</exception>
	public static string Quote(string value)
	{
		ArgumentNullException.ThrowIfNull(value);
		if (value.Contains('\0', StringComparison.Ordinal))
		{
			throw new ArgumentException("Shell arguments cannot contain NUL characters.", nameof(value));
		}

		return string.Concat("'", value.Replace("'", @"'\''", StringComparison.Ordinal), "'");
	}

	/// <summary>
	/// <c>sh -c SCRIPT sh ARG...</c>: the script sees the arguments as <c>$1</c>, <c>$2</c> and so on, so paths never
	/// become part of the script text.
	/// </summary>
	public static string ScriptCommand(string script, IEnumerable<string> arguments)
	{
		ArgumentNullException.ThrowIfNull(arguments);
		StringBuilder builder = new("sh -c ");
		builder.Append(Quote(script)).Append(" sh");
		foreach (string argument in arguments)
		{
			builder.Append(' ').Append(Quote(argument));
		}

		return builder.ToString();
	}

	/// <summary>
	/// The line that makes an interactive shell change into <paramref name="directory"/>, Enter included, or null when the
	/// path holds a control character. The line is typed, not run: quoting cannot stop a Ctrl+C or Ctrl+U in a folder name
	/// from ending the line early, and whatever follows it would then run as a command.
	/// </summary>
	public static string? ChangeDirectoryCommand(string directory)
	{
		ArgumentNullException.ThrowIfNull(directory);
		return directory.Any(char.IsControl) ? null : "cd -- " + Quote(directory) + "\r";
	}
}
