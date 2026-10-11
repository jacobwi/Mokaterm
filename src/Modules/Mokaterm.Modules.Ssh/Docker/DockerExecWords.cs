using System.Text;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// Splits a command typed for a container with no shell into its words, the way a POSIX shell would split a simple
/// command: blanks separate words, single quotes keep everything, double quotes keep everything but <c>\"</c> and
/// <c>\\</c>, and a backslash outside quotes keeps the next character. Nothing is expanded, since no shell runs.
/// </summary>
internal static class DockerExecWords
{
	private const char Backslash = (char)0x5C;

	/// <returns>The words, or a problem for a quote that never closes.</returns>
	public static (IReadOnlyList<string> Words, string? Problem) Split(string text)
	{
		ArgumentNullException.ThrowIfNull(text);
		List<string> words = [];
		StringBuilder word = new();
		bool inWord = false;
		char quote = '\0';
		for (int index = 0; index < text.Length; index++)
		{
			char character = text[index];
			if (quote == '\'')
			{
				if (character == '\'')
				{
					quote = '\0';
				}
				else
				{
					word.Append(character);
				}

				continue;
			}

			if (quote == '"')
			{
				if (character == '"')
				{
					quote = '\0';
				}
				else if (character == Backslash && index + 1 < text.Length && text[index + 1] is '"' or Backslash)
				{
					word.Append(text[++index]);
				}
				else
				{
					word.Append(character);
				}

				continue;
			}

			if (character is '\'' or '"')
			{
				quote = character;
				inWord = true;
			}
			else if (character == Backslash && index + 1 < text.Length)
			{
				word.Append(text[++index]);
				inWord = true;
			}
			else if (character is ' ' or '\t')
			{
				if (inWord)
				{
					words.Add(word.ToString());
					word.Clear();
					inWord = false;
				}
			}
			else
			{
				word.Append(character);
				inWord = true;
			}
		}

		if (quote != '\0')
		{
			return ([], $"A {(quote == '"' ? "double" : "single")} quote is never closed.");
		}

		if (inWord)
		{
			words.Add(word.ToString());
		}

		return words.Count == 0 ? ([], "Type a command to run.") : (words, null);
	}
}
