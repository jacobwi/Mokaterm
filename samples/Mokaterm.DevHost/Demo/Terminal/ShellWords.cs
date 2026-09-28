using System.Text;

namespace Mokaterm.DevHost.Demo.Terminal;

/// <summary>Splits a command line into words the way bash does for the simple cases: blanks, quotes and backslashes.</summary>
internal static class ShellWords
{
	/// <summary>False when a quote is left open.</summary>
	public static bool TryParse(string line, out List<string> words)
	{
		words = [];
		StringBuilder word = new();
		bool inWord = false;
		char quote = '\0';
		for (int i = 0; i < line.Length; i++)
		{
			char c = line[i];
			if (quote == '\'')
			{
				if (c == '\'')
				{
					quote = '\0';
				}
				else
				{
					word.Append(c);
				}

				continue;
			}

			if (quote == '"')
			{
				if (c == '"')
				{
					quote = '\0';
				}
				else if (c == '\\' && i + 1 < line.Length && line[i + 1] is '"' or '\\' or '$' or '`')
				{
					word.Append(line[++i]);
				}
				else
				{
					word.Append(c);
				}

				continue;
			}

			switch (c)
			{
				case ' ' or '\t':
					if (inWord)
					{
						words.Add(word.ToString());
						word.Clear();
						inWord = false;
					}

					break;
				case '\'' or '"':
					quote = c;
					inWord = true;
					break;
				case '\\' when i + 1 < line.Length:
					word.Append(line[++i]);
					inWord = true;
					break;
				default:
					word.Append(c);
					inWord = true;
					break;
			}
		}

		if (quote != '\0')
		{
			return false;
		}

		if (inWord)
		{
			words.Add(word.ToString());
		}

		return true;
	}
}
