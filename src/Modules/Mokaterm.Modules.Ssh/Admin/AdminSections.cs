namespace Mokaterm.Modules.Ssh.Admin;

/// <summary>
/// Output of a script that runs several commands, split back into what each printed. A script prints
/// <c>@@mt:name</c> on a line of its own before each part; a part can be printed more than once (one per file).
/// </summary>
internal sealed class AdminSections
{
	public const string Prefix = "@@mt:";

	private readonly List<(string Name, string Text)> _parts;

	private AdminSections(List<(string Name, string Text)> parts) => _parts = parts;

	/// <summary>Every part in the order printed. A part's name can carry a value after a space (<c>file /etc/crontab</c>).</summary>
	public IReadOnlyList<(string Name, string Text)> Parts => _parts;

	/// <summary>The line a script prints to start a part.</summary>
	public static string Start(string name) => $"echo '{Prefix}{name}'";

	public static AdminSections Parse(string output)
	{
		ArgumentNullException.ThrowIfNull(output);
		List<(string Name, string Text)> parts = [];
		string? name = null;
		int start = 0;
		int position = 0;
		while (position <= output.Length)
		{
			int end = output.IndexOf('\n', position);
			int lineEnd = end < 0 ? output.Length : end;
			if (string.CompareOrdinal(output, position, Prefix, 0, Prefix.Length) == 0 && lineEnd - position >= Prefix.Length)
			{
				if (name is not null)
				{
					parts.Add((name, output[start..position]));
				}

				name = output[(position + Prefix.Length)..lineEnd].TrimEnd();
				start = end < 0 ? output.Length : end + 1;
			}

			if (end < 0)
			{
				break;
			}

			position = end + 1;
		}

		if (name is not null)
		{
			parts.Add((name, output[start..]));
		}

		return new AdminSections(parts);
	}

	/// <summary>The first part called <paramref name="name"/>, or null when the script never printed it.</summary>
	public string? Get(string name)
	{
		foreach ((string partName, string text) in _parts)
		{
			if (partName == name)
			{
				return text;
			}
		}

		return null;
	}

	public bool Has(string name) => Get(name) is not null;

	/// <summary>Every part whose name starts with <paramref name="name"/> and a space, with the rest of the name.</summary>
	public IEnumerable<(string Value, string Text)> All(string name)
	{
		string prefix = name + " ";
		foreach ((string partName, string text) in _parts)
		{
			if (partName.StartsWith(prefix, StringComparison.Ordinal))
			{
				yield return (partName[prefix.Length..], text);
			}
		}
	}

	/// <summary>The non-blank lines of a part, trimmed at the end.</summary>
	public static IEnumerable<string> Lines(string? text)
	{
		if (text is null)
		{
			yield break;
		}

		foreach (string line in text.Split('\n'))
		{
			string trimmed = line.TrimEnd('\r', ' ', '\t');
			if (trimmed.Length > 0)
			{
				yield return trimmed;
			}
		}
	}
}
