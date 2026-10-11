using System.Text;

namespace Mokaterm.Modules.Local.Pty.Windows;

/// <summary>
/// Builds the one string Windows passes a program as its command line. Each program splits it again itself; the
/// quoting here is the rule the C runtime and CommandLineToArgvW split by, which is what every program this module
/// starts uses.
/// </summary>
internal static class WindowsCommandLine
{
	/// <summary>
	/// The program, the arguments this module chose (each quoted), and the text the login typed, as typed: a user who
	/// writes a command line for cmd.exe means cmd.exe's own rules, which no quoting here could know.
	/// </summary>
	public static string Build(string program, IReadOnlyList<string> arguments, string? extra)
	{
		ArgumentNullException.ThrowIfNull(program);
		ArgumentNullException.ThrowIfNull(arguments);

		// The first argument is split without any escape processing, so the path is quoted and nothing else: a Windows
		// path cannot hold a quote.
		StringBuilder line = new();
		bool quoteProgram = program.Length == 0 || program.AsSpan().IndexOfAny(' ', '\t') >= 0;
		line.Append(quoteProgram ? $"\"{program}\"" : program);
		foreach (string argument in arguments)
		{
			line.Append(' ').Append(Quote(argument));
		}

		if (!string.IsNullOrWhiteSpace(extra))
		{
			line.Append(' ').Append(extra.Trim());
		}

		return line.ToString();
	}

	/// <summary>
	/// One argument the way CommandLineToArgvW reads it back: untouched when it has nothing to protect, otherwise in
	/// quotes, with every backslash that comes before a quote (an inner one or the closing one) doubled.
	/// </summary>
	public static string Quote(string argument)
	{
		ArgumentNullException.ThrowIfNull(argument);
		if (argument.Length > 0 && argument.AsSpan().IndexOfAny(" \t\n\v\"") < 0)
		{
			return argument;
		}

		StringBuilder quoted = new(argument.Length + 2);
		quoted.Append('"');
		int index = 0;
		while (true)
		{
			int backslashes = 0;
			while (index < argument.Length && argument[index] == '\\')
			{
				backslashes++;
				index++;
			}

			if (index == argument.Length)
			{
				// Before the closing quote, every one of them has to be doubled or the last would escape it.
				quoted.Append('\\', backslashes * 2);
				break;
			}

			if (argument[index] == '"')
			{
				quoted.Append('\\', (backslashes * 2) + 1).Append('"');
			}
			else
			{
				quoted.Append('\\', backslashes).Append(argument[index]);
			}

			index++;
		}

		return quoted.Append('"').ToString();
	}
}
