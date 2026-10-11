using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Mokaterm.Modules.Local.Shells;

/// <summary>Reads the <c>NAME=value</c> lines a login adds to its shell's environment.</summary>
internal static class LocalEnvironmentVariables
{
	/// <summary>
	/// One variable per line. Blank lines and lines starting with <c>#</c> are skipped, the name is trimmed and the value
	/// is kept as written, and <c>NAME=</c> with nothing after it means "remove it". A later line for the same name
	/// wins, as it would in a script.
	/// </summary>
	public static bool TryParse(
		string? text,
		out IReadOnlyList<KeyValuePair<string, string>> variables,
		[NotNullWhen(false)] out string? error)
	{
		List<KeyValuePair<string, string>> parsed = [];
		variables = parsed;
		error = null;
		if (string.IsNullOrWhiteSpace(text))
		{
			return true;
		}

		string[] lines = text.ReplaceLineEndings("\n").Split('\n');
		for (int index = 0; index < lines.Length; index++)
		{
			string line = lines[index].Trim();
			if (line.Length == 0 || line[0] == '#')
			{
				continue;
			}

			int separator = line.IndexOf('=', StringComparison.Ordinal);
			if (separator < 0)
			{
				error = Line(index, "needs a value after an equals sign, as in NAME=value");
				return false;
			}

			string name = line[..separator].Trim();
			if (name.Length == 0)
			{
				error = Line(index, "has no name before its equals sign");
				return false;
			}

			parsed.RemoveAll(variable => variable.Key.Equals(name, StringComparison.OrdinalIgnoreCase));
			parsed.Add(new KeyValuePair<string, string>(name, line[(separator + 1)..]));
		}

		return true;
	}

	private static string Line(int index, string problem) =>
		string.Create(CultureInfo.CurrentCulture, $"Line {index + 1} {problem}.");
}
