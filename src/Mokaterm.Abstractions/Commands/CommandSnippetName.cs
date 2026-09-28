using System.Text;

namespace Mokaterm.Abstractions.Commands;

/// <summary>The name a saved command gets when the user does not type one.</summary>
public static class CommandSnippetName
{
	/// <summary>Longest name derived from a command, before the trailing dots.</summary>
	public const int MaxLength = 48;

	private const string Ellipsis = "...";

	/// <summary>
	/// The command itself as a name: one line, single spaces, cut on a word boundary. Blank in, blank out, so callers
	/// can fall back to their own text.
	/// </summary>
	public static string Derive(string? command)
	{
		string single = Collapse(command);
		if (single.Length <= MaxLength)
		{
			return single;
		}

		int cut = single.LastIndexOf(' ', MaxLength - Ellipsis.Length);

		// A single long word (a path, a base64 blob) has no space to cut at.
		int end = cut > MaxLength / 3 ? cut : MaxLength - Ellipsis.Length;
		return single[..end].TrimEnd() + Ellipsis;
	}

	/// <summary>The first line of <paramref name="text"/> with runs of whitespace turned into single spaces.</summary>
	public static string Collapse(string? text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return "";
		}

		StringBuilder builder = new(text.Length);
		bool pendingSpace = false;
		foreach (char character in text)
		{
			if (character is '\r' or '\n')
			{
				break;
			}

			if (char.IsWhiteSpace(character))
			{
				pendingSpace = builder.Length > 0;
				continue;
			}

			if (pendingSpace)
			{
				builder.Append(' ');
				pendingSpace = false;
			}

			builder.Append(character);
		}

		return builder.ToString();
	}
}
