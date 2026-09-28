using System.Text.RegularExpressions;

namespace Mokaterm.Core.Terminal;

/// <summary>Line handling shared by the plain text scheme formats (Alacritty, X resources) and their exporters.</summary>
internal static partial class TerminalThemeText
{
	// A longer comment, or one holding a colon or an equals sign, is a license header or a modeline, not a name.
	private const int MaxCommentNameLength = 48;

	private static readonly char[] Decoration = ['!', '#', '/', '*', '-', '=', ' ', '\t'];

	public static string[] Lines(string text) => text.Split('\n');

	/// <summary>
	/// The title in the comments at the top of <paramref name="text"/>. Alacritty and X resources have no name of
	/// their own, so published themes put one there and the exporters write one back.
	/// </summary>
	public static string? NameFromComment(string text, string marker)
	{
		foreach (string line in Lines(text))
		{
			string trimmed = line.Trim();
			if (trimmed.Length == 0)
			{
				continue;
			}

			if (!trimmed.StartsWith(marker, StringComparison.Ordinal))
			{
				return null;
			}

			if (Name(trimmed[marker.Length..]) is { } name)
			{
				return name;
			}
		}

		return null;
	}

	/// <summary>Drops a trailing comment that starts outside quotes, for TOML and YAML values.</summary>
	public static string StripComment(string value, char marker)
	{
		char quote = '\0';
		for (int index = 0; index < value.Length; index++)
		{
			char character = value[index];
			if (quote != '\0')
			{
				if (character == quote)
				{
					quote = '\0';
				}
			}
			else if (character is '"' or '\'')
			{
				quote = character;
			}
			else if (character == marker)
			{
				return value[..index];
			}
		}

		return value;
	}

	/// <summary>Splits <c>key = value</c> or <c>key: value</c>, returning false when the line carries no pair.</summary>
	public static bool TrySplit(string line, char separator, out string key, out string value)
	{
		int index = line.IndexOf(separator);
		if (index <= 0)
		{
			key = "";
			value = "";
			return false;
		}

		key = line[..index].Trim();
		value = line[(index + 1)..].Trim();
		return key.Length > 0;
	}

	private static string? Name(string comment)
	{
		string name = comment.Trim(Decoration);

		// Alacritty themes title themselves "Colors (Gruvbox dark)"; the scheme name is what is in the brackets.
		if (ColorsTitle().Match(name) is { Success: true } title)
		{
			name = title.Groups["name"].Value;
		}

		return name.Length is > 0 and <= MaxCommentNameLength
			&& !name.Contains(':', StringComparison.Ordinal)
			&& !name.Contains('=', StringComparison.Ordinal)
				? name
				: null;
	}

	[GeneratedRegex(@"^Colors\s*\((?<name>[^()]+)\)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
	private static partial Regex ColorsTitle();
}
