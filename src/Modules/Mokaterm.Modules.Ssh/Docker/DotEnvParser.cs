using System.Text;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// Reads an env file the way compose does: <c>NAME=value</c> lines, an optional <c>export</c> in front, comments from
/// <c>#</c>, single quotes taken as they are, double quotes with <c>\n</c>, <c>\t</c>, <c>\"</c> and <c>\\</c>, both
/// allowed to run over several lines, and a <c>#</c> after a blank ending an unquoted value. A line it cannot read is
/// skipped rather than guessed at: the list is for looking, and the file itself is what compose uses.
/// </summary>
internal static class DotEnvParser
{
	private const char Backslash = (char)0x5C;

	public static IReadOnlyList<DotEnvVariable> Parse(string? text)
	{
		List<DotEnvVariable> variables = [];
		string[] lines = (text ?? "").TrimStart((char)0xFEFF).ReplaceLineEndings("\n").Split('\n');
		for (int index = 0; index < lines.Length; index++)
		{
			string line = lines[index].Trim();
			int number = index + 1;
			if (line.Length == 0 || line.StartsWith('#'))
			{
				continue;
			}

			if (line.StartsWith("export", StringComparison.Ordinal) && line.Length > 6 && char.IsWhiteSpace(line[6]))
			{
				line = line[7..].TrimStart();
			}

			int equals = line.IndexOf('=', StringComparison.Ordinal);
			string name = (equals < 0 ? line : line[..equals]).Trim();
			if (name.Length == 0 || name.Any(char.IsWhiteSpace))
			{
				continue;
			}

			if (equals < 0)
			{
				variables.Add(new DotEnvVariable(name, null, number));
				continue;
			}

			string rest = line[(equals + 1)..].TrimStart();
			if (rest.StartsWith('\'') || rest.StartsWith('"'))
			{
				if (ReadQuoted(lines, ref index, rest) is { } quoted)
				{
					variables.Add(new DotEnvVariable(name, quoted, number));
				}

				continue;
			}

			variables.Add(new DotEnvVariable(name, Unquoted(rest), number));
		}

		return variables;
	}

	/// <summary>An unquoted value: up to a <c>#</c> that follows a blank, without blanks at either end.</summary>
	private static string Unquoted(string value)
	{
		for (int index = 1; index < value.Length; index++)
		{
			if (value[index] == '#' && char.IsWhiteSpace(value[index - 1]))
			{
				return value[..index].TrimEnd();
			}
		}

		return value.TrimEnd();
	}

	/// <summary>
	/// A quoted value that starts in <paramref name="first"/> and may go on over the lines after it. Null, and the
	/// lines left unread, when the quote never closes.
	/// </summary>
	private static string? ReadQuoted(string[] lines, ref int index, string first)
	{
		char quote = first[0];
		StringBuilder value = new();
		string current = first[1..];
		int line = index;
		while (true)
		{
			for (int position = 0; position < current.Length; position++)
			{
				char character = current[position];
				if (character == quote)
				{
					index = line;
					return value.ToString();
				}

				if (quote == '"' && character == Backslash && position + 1 < current.Length)
				{
					char next = current[++position];
					value.Append(next switch
					{
						'n' => '\n',
						't' => '\t',
						'r' => '\r',
						_ => next,
					});
					continue;
				}

				value.Append(character);
			}

			if (++line >= lines.Length)
			{
				return null;
			}

			value.Append('\n');
			current = lines[line];
		}
	}
}
