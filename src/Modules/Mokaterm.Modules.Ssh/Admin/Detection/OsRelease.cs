using System.Text;

namespace Mokaterm.Modules.Ssh.Admin.Detection;

/// <summary>
/// <c>/etc/os-release</c>: shell-style assignments, values quoted with double or single quotes or not at all. Only
/// the fields the system view reads are kept as properties; the rest stay in <see cref="Values"/>.
/// </summary>
internal sealed record OsRelease
{
	public static OsRelease Empty { get; } = new();

	public IReadOnlyDictionary<string, string> Values { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

	/// <summary>Lower-case, such as <c>debian</c>, <c>rocky</c>, <c>opensuse-leap</c>.</summary>
	public string Id => Get("ID").ToLowerInvariant();

	/// <summary>The distributions this one derives from, closest first.</summary>
	public IReadOnlyList<string> IdLike => Get("ID_LIKE").ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);

	public string VersionId => Get("VERSION_ID");

	public string Name => Get("NAME");

	public string PrettyName => Get("PRETTY_NAME");

	public string Get(string key) => Values.TryGetValue(key, out string? value) ? value : "";

	public static OsRelease Parse(string? text)
	{
		Dictionary<string, string> values = new(StringComparer.Ordinal);
		foreach (string raw in (text ?? "").Split('\n'))
		{
			string line = raw.Trim();
			if (line.Length == 0 || line[0] == '#')
			{
				continue;
			}

			int equals = line.IndexOf('=', StringComparison.Ordinal);
			if (equals <= 0)
			{
				continue;
			}

			string key = line[..equals];
			if (!IsKey(key))
			{
				continue;
			}

			values[key] = Unquote(line[(equals + 1)..]);
		}

		return new OsRelease { Values = values };
	}

	private static bool IsKey(string key)
	{
		foreach (char character in key)
		{
			if (!(char.IsAsciiLetterUpper(character) || char.IsAsciiDigit(character) || character == '_'))
			{
				return false;
			}
		}

		return true;
	}

	// The format is a subset of shell: double quotes take backslash escapes for $, ", \ and `; single quotes take nothing.
	private static string Unquote(string value)
	{
		StringBuilder builder = new(value.Length);
		char quote = '\0';
		for (int index = 0; index < value.Length; index++)
		{
			char character = value[index];
			if (quote == '\0')
			{
				if (character is '"' or '\'')
				{
					quote = character;
				}
				else if (character == '\\' && index + 1 < value.Length)
				{
					builder.Append(value[++index]);
				}
				else if (char.IsWhiteSpace(character) || character == '#')
				{
					break;
				}
				else
				{
					builder.Append(character);
				}
			}
			else if (character == quote)
			{
				quote = '\0';
			}
			else if (quote == '"' && character == '\\' && index + 1 < value.Length && value[index + 1] is '$' or '"' or '\\' or '`')
			{
				builder.Append(value[++index]);
			}
			else
			{
				builder.Append(character);
			}
		}

		return builder.ToString();
	}
}
