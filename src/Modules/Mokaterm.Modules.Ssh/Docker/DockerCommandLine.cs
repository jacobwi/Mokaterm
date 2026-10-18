using System.Text;
using Mokaterm.Abstractions.Commands;

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

	/// <param name="masked">Shows the secret values of <see cref="DockerCall.MaskedArguments"/> as a mask, for a line that is only read.</param>
	public static string Build(DockerCall call, bool masked = false)
	{
		ArgumentNullException.ThrowIfNull(call);
		StringBuilder line = new();
		if (call.WorkingDirectory is { } directory)
		{
			line.Append("cd -- ").Append(Word(directory)).Append(" && ");
		}

		line.AppendJoin(' ', call.ProgramWords);
		for (int index = 0; index < call.Arguments.Count; index++)
		{
			string argument = call.Arguments[index];
			line.Append(' ').Append(Word(masked && call.MaskedArguments.Contains(index) ? Mask(argument) : argument));
		}

		return line.ToString();
	}

	/// <summary><c>NAME=value</c> as <c>NAME=</c> and the mask the Env tab shows; an empty value has nothing to hide.</summary>
	private static string Mask(string argument)
	{
		int equals = argument.IndexOf('=', StringComparison.Ordinal);
		return equals < 0 || equals == argument.Length - 1 ? argument : argument[..(equals + 1)] + DockerInspectParser.Mask;
	}

	/// <summary>
	/// What goes in front of a script whose call reads stdin: sudo takes its password line only when it needs one, so the
	/// call's input follows a marker line and everything before the marker is dropped (<see cref="Admin.AdminScriptText"/>).
	/// </summary>
	public static readonly string StdinReader = Admin.AdminScriptText.InputReader.ReplaceLineEndings("\n");

	/// <summary>The start of a script of the module's own that runs in a folder given as <c>$1</c>.</summary>
	private const string InDirectory = "cd -- \"$1\" || exit 1\nshift\n";

	/// <summary>
	/// The script and its positional arguments for running <paramref name="call"/> through sudo. A call with stdin gets
	/// <see cref="StdinReader"/> first; its input then goes framed behind the marker.
	/// </summary>
	public static (string Script, IReadOnlyList<string> Arguments) ForScript(DockerCall call)
	{
		ArgumentNullException.ThrowIfNull(call);
		string script;
		List<string> arguments = [];
		if (call.Program == DockerProgram.Shell)
		{
			// sudo already runs its own sh -c around a script, so a script of ours goes in as that script, $0 dropped.
			script = call.WorkingDirectory is null ? call.Arguments[0] : InDirectory + call.Arguments[0];
			if (call.WorkingDirectory is { } folder)
			{
				arguments.Add(folder);
			}

			arguments.AddRange(call.Arguments.Skip(2));
		}
		else
		{
			if (call.WorkingDirectory is { } directory)
			{
				arguments.Add(directory);
			}

			arguments.AddRange(call.ProgramWords);
			arguments.AddRange(call.Arguments);
			script = call.WorkingDirectory is null ? ElevatedScript : ElevatedScriptInDirectory;
		}

		return (call.Input is null ? script : StdinReader + script, arguments);
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
