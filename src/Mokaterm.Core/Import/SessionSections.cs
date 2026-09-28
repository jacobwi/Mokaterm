namespace Mokaterm.Core.Import;

/// <summary>Picks the sections that sit under a session container and decodes their names.</summary>
internal static class SessionSections
{
	public static IEnumerable<SessionValues> Under(IEnumerable<ImportSection> sections, string marker)
	{
		foreach (ImportSection section in sections)
		{
			int index = section.Path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
			if (index < 0)
			{
				continue;
			}

			string encoded = section.Path[(index + marker.Length)..].Trim('\\');
			if (encoded.Length == 0)
			{
				continue;
			}

			SessionValues values = new(SessionNameCodec.Decode(encoded));
			foreach (KeyValuePair<string, string> pair in section.Values)
			{
				values.Set(pair.Key, pair.Value);
			}

			yield return values;
		}
	}
}
