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

	public static AdminSections Parse(string output) => Parse(output, Prefix);

	/// <summary>
	/// Output split on lines that start with <paramref name="prefix"/> instead of <see cref="Prefix"/>, for a script that
	/// runs other scripts whole as its parts: each part keeps its own <c>@@mt:</c> sections for its own parser.
	/// </summary>
	public static AdminSections Parse(string output, string prefix)
	{
		ArgumentNullException.ThrowIfNull(output);
		ArgumentException.ThrowIfNullOrEmpty(prefix);
		List<(string Name, string Text)> parts = [];
		string? name = null;
		int start = 0;
		int position = 0;
		while (position <= output.Length)
		{
			int end = output.IndexOf('\n', position);
			int lineEnd = end < 0 ? output.Length : end;
			if (string.CompareOrdinal(output, position, prefix, 0, prefix.Length) == 0 && lineEnd - position >= prefix.Length)
			{
				if (name is not null)
				{
					parts.Add((name, output[start..position]));
				}

				name = output[(position + prefix.Length)..lineEnd].TrimEnd();
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

	/// <summary>
	/// Everything after the first line that starts part <paramref name="name"/>, as it came: for a script that ends by
	/// printing a log or a file, whose own lines could look like the start of a part. Null when the part never started.
	/// </summary>
	public static string? Rest(string output, string name)
	{
		ArgumentNullException.ThrowIfNull(output);
		string line = Prefix + name;
		int at = output.StartsWith(line + "\n", StringComparison.Ordinal) || output == line
			? 0
			: output.IndexOf("\n" + line + "\n", StringComparison.Ordinal) is >= 0 and var found
				? found + 1
				: output.EndsWith("\n" + line, StringComparison.Ordinal) ? output.Length - line.Length : -1;
		if (at < 0)
		{
			return null;
		}

		int start = at + line.Length + 1;
		return start >= output.Length ? "" : output[start..];
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
