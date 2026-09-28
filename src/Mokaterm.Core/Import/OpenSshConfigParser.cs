using Mokaterm.Abstractions.Import;

namespace Mokaterm.Core.Import;

/// <summary>
/// Reads an <c>ssh_config</c> into ordered <c>Host</c> blocks. It keeps OpenSSH's own rules: keywords are
/// case-insensitive, an <c>=</c> may stand in for the separator, values may be quoted, settings before the first
/// <c>Host</c> line behave like <c>Host *</c>, and the first value seen for a keyword wins.
/// </summary>
internal static class OpenSshConfigParser
{
	/// <summary>OpenSSH allows 16; eight is more than any real config and keeps a loop cheap to refuse.</summary>
	public const int MaxIncludeDepth = 8;

	/// <summary>
	/// Parses <paramref name="text"/>. <paramref name="resolveInclude"/> turns an <c>Include</c> argument into the
	/// files it names; null means includes cannot be followed and each one is reported instead.
	/// </summary>
	public static OpenSshConfigParseResult Parse(string text, Func<string, IReadOnlyList<OpenSshConfigFile>>? resolveInclude)
	{
		ArgumentNullException.ThrowIfNull(text);
		List<OpenSshConfigBlock> blocks = [];
		List<ImportSkip> notes = [];
		HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase);
		ParseInto(text, resolveInclude, blocks, notes, visited, depth: 0);
		return new OpenSshConfigParseResult(blocks, notes);
	}

	private static void ParseInto(
		string text,
		Func<string, IReadOnlyList<OpenSshConfigFile>>? resolveInclude,
		List<OpenSshConfigBlock> blocks,
		List<ImportSkip> notes,
		HashSet<string> visited,
		int depth)
	{
		// Anything before the first Host line applies everywhere, which is what "Host *" means.
		List<string> patterns = ["*"];
		List<KeyValuePair<string, string>> settings = [];
		bool inMatch = false;

		void Flush()
		{
			if (settings.Count > 0 && patterns.Count > 0)
			{
				blocks.Add(new OpenSshConfigBlock([.. patterns], [.. settings]));
			}

			settings = [];
		}

		foreach (string line in text.Split('\n'))
		{
			if (!TryReadLine(line, out string keyword, out List<string> arguments))
			{
				continue;
			}

			if (keyword.Equals("host", StringComparison.OrdinalIgnoreCase))
			{
				Flush();
				patterns = arguments;
				inMatch = false;
				continue;
			}

			if (keyword.Equals("match", StringComparison.OrdinalIgnoreCase))
			{
				Flush();
				patterns = [];
				inMatch = true;
				notes.Add(new ImportSkip(
					$"Match {string.Join(' ', arguments)}",
					"Match blocks depend on how you are connecting, so their settings are not imported."));
				continue;
			}

			if (keyword.Equals("include", StringComparison.OrdinalIgnoreCase))
			{
				Flush();
				Include(arguments, resolveInclude, blocks, notes, visited, depth);
				continue;
			}

			if (arguments.Count > 0 && !inMatch)
			{
				settings.Add(new KeyValuePair<string, string>(keyword, string.Join(' ', arguments)));
			}
		}

		Flush();
	}

	private static void Include(
		List<string> arguments,
		Func<string, IReadOnlyList<OpenSshConfigFile>>? resolveInclude,
		List<OpenSshConfigBlock> blocks,
		List<ImportSkip> notes,
		HashSet<string> visited,
		int depth)
	{
		foreach (string argument in arguments)
		{
			if (resolveInclude is null)
			{
				notes.Add(new ImportSkip($"Include {argument}", "Included files are only read when the config is opened from disk."));
				continue;
			}

			if (depth >= MaxIncludeDepth)
			{
				notes.Add(new ImportSkip($"Include {argument}", "Includes are nested too deeply."));
				continue;
			}

			IReadOnlyList<OpenSshConfigFile> files = resolveInclude(argument);
			if (files.Count == 0)
			{
				notes.Add(new ImportSkip($"Include {argument}", "No file matched this include."));
				continue;
			}

			foreach (OpenSshConfigFile file in files)
			{
				if (!visited.Add(file.Name))
				{
					notes.Add(new ImportSkip($"Include {file.Name}", "This file includes itself."));
					continue;
				}

				ParseInto(file.Text, resolveInclude, blocks, notes, visited, depth + 1);
			}
		}
	}

	/// <summary>Splits a line into its keyword and arguments, or returns false for a blank or comment line.</summary>
	private static bool TryReadLine(string line, out string keyword, out List<string> arguments)
	{
		keyword = "";
		arguments = [];
		ReadOnlySpan<char> span = line.AsSpan().Trim();
		if (span.IsEmpty || span[0] == '#')
		{
			return false;
		}

		int index = 0;
		while (index < span.Length && !char.IsWhiteSpace(span[index]) && span[index] != '=')
		{
			index++;
		}

		keyword = span[..index].ToString();
		if (keyword.Length == 0)
		{
			return false;
		}

		bool separatorSeen = false;
		while (index < span.Length && (char.IsWhiteSpace(span[index]) || (span[index] == '=' && !separatorSeen)))
		{
			separatorSeen |= span[index] == '=';
			index++;
		}

		arguments = SplitArguments(span[index..]);
		return true;
	}

	private static List<string> SplitArguments(ReadOnlySpan<char> span)
	{
		List<string> arguments = [];
		int index = 0;
		while (index < span.Length)
		{
			while (index < span.Length && char.IsWhiteSpace(span[index]))
			{
				index++;
			}

			if (index >= span.Length || span[index] == '#')
			{
				break;
			}

			if (span[index] == '"')
			{
				index++;
				int start = index;
				while (index < span.Length && span[index] != '"')
				{
					index++;
				}

				arguments.Add(span[start..index].ToString());
				if (index < span.Length)
				{
					index++;
				}
			}
			else
			{
				int start = index;
				while (index < span.Length && !char.IsWhiteSpace(span[index]))
				{
					index++;
				}

				arguments.Add(span[start..index].ToString());
			}
		}

		return arguments;
	}
}
