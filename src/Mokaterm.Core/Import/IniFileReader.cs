namespace Mokaterm.Core.Import;

/// <summary>Reads an ini file into the same sections a registry export produces. WinSCP's portable store is one.</summary>
internal static class IniFileReader
{
	public static IReadOnlyList<ImportSection> Parse(ReadOnlySpan<byte> content) => ParseText(TextFileDecoder.Decode(content));

	internal static IReadOnlyList<ImportSection> ParseText(string text)
	{
		List<ImportSection> sections = [];
		Dictionary<string, string>? values = null;
		string path = "";

		foreach (string raw in text.Split('\n'))
		{
			string line = raw.Trim();
			if (line.Length == 0 || line[0] == ';' || line[0] == '#')
			{
				continue;
			}

			if (line[0] == '[' && line[^1] == ']')
			{
				Flush();
				path = line[1..^1].Trim();
				values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				continue;
			}

			int separator = line.IndexOf('=', StringComparison.Ordinal);
			if (values is null || separator <= 0)
			{
				continue;
			}

			values[line[..separator].Trim()] = line[(separator + 1)..].Trim();
		}

		Flush();
		return sections;

		void Flush()
		{
			if (values is { Count: > 0 })
			{
				sections.Add(new ImportSection(path, values));
			}

			values = null;
		}
	}
}
