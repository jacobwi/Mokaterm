namespace Mokaterm.DevHost.Demo.Docker;

/// <summary>
/// Just enough of compose's own reading of its files for the demo to refuse a broken one with compose's wording: a tab
/// in an indent or a line that is neither a key, a list item nor a comment, and an env line whose name is not a name.
/// It is not a YAML parser, and the resolved config the demo prints stays the one it was written with.
/// </summary>
internal static class DemoComposeCheck
{
	/// <summary>What compose says about <paramref name="text"/> as a compose file, or null when it reads.</summary>
	public static string? Yaml(string text)
	{
		ArgumentNullException.ThrowIfNull(text);
		string[] lines = text.Split('\n');
		for (int index = 0; index < lines.Length; index++)
		{
			string line = lines[index].TrimEnd('\r');
			string body = line.TrimStart();
			if (body.Length == 0 || body.StartsWith('#'))
			{
				continue;
			}

			if (line[..^body.Length].Contains('\t', StringComparison.Ordinal))
			{
				return $"yaml: line {index + 1}: found a tab character that violates indentation";
			}

			if (!body.StartsWith('-') && !body.Contains(':', StringComparison.Ordinal))
			{
				return $"yaml: line {index + 1}: could not find expected ':'";
			}
		}

		return null;
	}

	/// <summary>What compose says about <paramref name="text"/> as the env file at <paramref name="path"/>, or null when it reads.</summary>
	public static string? Env(string path, string text)
	{
		ArgumentNullException.ThrowIfNull(text);
		string[] lines = text.Split('\n');
		for (int index = 0; index < lines.Length; index++)
		{
			string line = lines[index].Trim();
			if (line.Length == 0 || line.StartsWith('#'))
			{
				continue;
			}

			if (line.StartsWith("export ", StringComparison.Ordinal))
			{
				line = line["export ".Length..].TrimStart();
			}

			int equals = line.IndexOf('=', StringComparison.Ordinal);
			string name = equals < 0 ? line : line[..equals];
			if (name.FirstOrDefault(character => !char.IsAsciiLetterOrDigit(character) && character is not ('_' or '.')) is var bad and not '\0')
			{
				return $"failed to read {path}: line {index + 1}: unexpected character \"{bad}\" in variable name \"{name}\"";
			}
		}

		return null;
	}
}
