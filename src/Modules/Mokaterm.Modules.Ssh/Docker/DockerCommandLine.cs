using System.Text;
using Mokaterm.Modules.Ssh.Shell;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// Turns a <see cref="DockerCall"/> into the line the login shell runs. A word made only of characters no shell treats
/// specially is left bare, so the line reads like one a person typed; anything else is single-quoted. The same text is
/// what a confirmation shows, so what the user approves is what runs.
/// </summary>
internal static class DockerCommandLine
{
	/// <summary>
	/// The script <c>sudo</c> runs: it changes into the folder in <c>$1</c> when the call names one, then runs the rest of
	/// its arguments as the command. Names stay arguments here too and never become part of the script's text.
	/// </summary>
	public const string ElevatedScript = "exec \"$@\"";

	/// <summary>The same script for a call with a working directory. Exit 1 is what <c>cd</c> itself returns.</summary>
	public const string ElevatedScriptInDirectory = "cd -- \"$1\" || exit 1\nshift\nexec \"$@\"";

	public static string Build(DockerCall call)
	{
		ArgumentNullException.ThrowIfNull(call);
		StringBuilder line = new();
		if (call.WorkingDirectory is { } directory)
		{
			line.Append("cd -- ").Append(Word(directory)).Append(" && ");
		}

		line.AppendJoin(' ', call.ProgramWords);
		foreach (string argument in call.Arguments)
		{
			line.Append(' ').Append(Word(argument));
		}

		return line.ToString();
	}

	/// <summary>The script and its positional arguments for running <paramref name="call"/> through sudo.</summary>
	public static (string Script, IReadOnlyList<string> Arguments) ForScript(DockerCall call)
	{
		ArgumentNullException.ThrowIfNull(call);
		if (call.Program == DockerProgram.Shell)
		{
			// sudo already runs its own sh -c around a script, so a script of ours goes in as that script, $0 dropped.
			return (call.Arguments[0], [.. call.Arguments.Skip(2)]);
		}

		List<string> arguments = [];
		if (call.WorkingDirectory is { } directory)
		{
			arguments.Add(directory);
		}

		arguments.AddRange(call.ProgramWords);
		arguments.AddRange(call.Arguments);
		return (call.WorkingDirectory is null ? ElevatedScript : ElevatedScriptInDirectory, arguments);
	}

	/// <summary>
	/// One shell word: bare when every character is a letter, a digit or one of <c>_ @ % + = : , . / -</c>, which no POSIX
	/// shell reads as anything but itself, and single-quoted otherwise. A leading <c>=</c> or <c>%</c> is quoted too,
	/// because zsh expands <c>=name</c> to a program's path and fish once read <c>%</c> as a process.
	/// </summary>
	public static string Word(string value)
	{
		ArgumentNullException.ThrowIfNull(value);
		bool plain = value.Length > 0 && value[0] is not ('=' or '%') && value.All(IsPlain);
		return plain ? value : PosixShell.Quote(value);
	}

	private static bool IsPlain(char character) =>
		char.IsAsciiLetterOrDigit(character) || character is '_' or '@' or '%' or '+' or '=' or ':' or ',' or '.' or '/' or '-';
}
