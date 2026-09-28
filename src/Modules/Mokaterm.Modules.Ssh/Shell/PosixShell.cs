using System.Text;

namespace Mokaterm.Modules.Ssh.Shell;

/// <summary>Builds POSIX shell command lines from untrusted words.</summary>
internal static class PosixShell
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
}
