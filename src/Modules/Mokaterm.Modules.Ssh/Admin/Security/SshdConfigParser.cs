namespace Mokaterm.Modules.Ssh.Admin.Security;

/// <summary>
/// Reads <c>sshd -T</c>, and the configuration files the way sshd reads them when that is all there is: comments and
/// blank lines skipped, the first value of a keyword kept, <c>Include</c> followed in name order where it stands, and
/// everything from the first <c>Match</c> on left out, since it only applies to some logins.
/// </summary>
internal static class SshdConfigParser
{
	private const int MaxIncludeDepth = 8;

	/// <summary>
	/// <c>sshd -T</c>: one <c>keyword value</c> per line, keywords in lower case. A keyword printed more than once
	/// (<c>allowusers</c> prints one line per entry) keeps every value, space separated.
	/// </summary>
	public static Dictionary<string, string> ParseEffective(string? text)
	{
		Dictionary<string, string> values = new(StringComparer.Ordinal);
		foreach (string line in AdminSections.Lines(text))
		{
			string trimmed = line.Trim();
			int space = trimmed.IndexOf(' ', StringComparison.Ordinal);
			if (space <= 0 || !trimmed[..space].All(char.IsAsciiLetterOrDigit))
			{
				continue;
			}

			string key = trimmed[..space].ToLowerInvariant();
			string value = trimmed[(space + 1)..].Trim();
			values[key] = values.TryGetValue(key, out string? earlier) ? earlier + " " + value : value;
		}

		return values;
	}

	/// <summary>The files, read from <see cref="SshdConfig.MainFile"/> on; <paramref name="files"/> holds every file that was read.</summary>
	public static SshdConfig ParseFiles(IReadOnlyDictionary<string, string> files, IReadOnlyList<string> unreadable)
	{
		ArgumentNullException.ThrowIfNull(files);
		ArgumentNullException.ThrowIfNull(unreadable);
		Reading reading = new(files);
		if (files.ContainsKey(SshdConfig.MainFile))
		{
			reading.Read(SshdConfig.MainFile, 0);
		}

		return new SshdConfig
		{
			Installed = true,
			Values = reading.Values,
			Sources = reading.Sources,
			Files = reading.Order,
			Unreadable = unreadable,
			IncludesDropIns = reading.IncludesDropIns,
			SetBeforeInclude = reading.SetBeforeInclude ?? new HashSet<string>(StringComparer.Ordinal),
		};
	}

	/// <summary><c>Keyword value</c> or <c>Keyword=value</c>, the value without the quotes around it.</summary>
	internal static (string Keyword, string Value) Split(string line)
	{
		int end = 0;
		while (end < line.Length && !char.IsWhiteSpace(line[end]) && line[end] != '=')
		{
			end++;
		}

		string value = line[end..].TrimStart();
		if (value.StartsWith('='))
		{
			value = value[1..].TrimStart();
		}

		value = value.TrimEnd();
		if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
		{
			value = value[1..^1];
		}

		return (line[..end], value);
	}

	/// <summary>A glob over whole paths: <c>*</c> and <c>?</c> stay inside one path segment, as glob(3) keeps them.</summary>
	internal static bool Matches(string pattern, string path) => Matches(pattern.AsSpan(), path.AsSpan());

	private static bool Matches(ReadOnlySpan<char> pattern, ReadOnlySpan<char> path)
	{
		while (!pattern.IsEmpty)
		{
			char head = pattern[0];
			if (head == '*')
			{
				ReadOnlySpan<char> rest = pattern[1..];
				for (int take = 0; take <= path.Length; take++)
				{
					if (Matches(rest, path[take..]))
					{
						return true;
					}

					if (take < path.Length && path[take] == '/')
					{
						return false;
					}
				}

				return false;
			}

			if (path.IsEmpty || (head == '?' ? path[0] == '/' : head != path[0]))
			{
				return false;
			}

			pattern = pattern[1..];
			path = path[1..];
		}

		return path.IsEmpty;
	}

	private sealed class Reading(IReadOnlyDictionary<string, string> files)
	{
		private readonly HashSet<string> _visited = new(StringComparer.Ordinal);
		private readonly HashSet<string> _mainKeys = new(StringComparer.Ordinal);

		public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

		public Dictionary<string, string> Sources { get; } = new(StringComparer.Ordinal);

		public List<string> Order { get; } = [];

		public bool IncludesDropIns { get; private set; }

		public HashSet<string>? SetBeforeInclude { get; private set; }

		public void Read(string path, int depth)
		{
			if (depth > MaxIncludeDepth || !_visited.Add(path) || !files.TryGetValue(path, out string? text))
			{
				return;
			}

			Order.Add(path);
			bool main = depth == 0;
			foreach (string raw in text.Split('\n'))
			{
				string line = raw.Trim();
				if (line.Length == 0 || line[0] == '#')
				{
					continue;
				}

				(string keyword, string value) = Split(line);
				string key = keyword.ToLowerInvariant();

				// From here on the lines only apply to the logins the block matches: not what every login gets.
				if (key == "match")
				{
					return;
				}

				if (key == "include")
				{
					Include(value, main, depth);
					continue;
				}

				if (main)
				{
					_mainKeys.Add(key);
				}

				if (Values.TryAdd(key, value))
				{
					Sources[key] = path;
				}
			}
		}

		private void Include(string patterns, bool main, int depth)
		{
			foreach (string pattern in patterns.Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
			{
				string full = pattern.StartsWith('/') ? pattern : "/etc/ssh/" + pattern;
				if (main && !IncludesDropIns && full == SshdConfig.DropInFolder + "/*.conf")
				{
					IncludesDropIns = true;
					SetBeforeInclude = new HashSet<string>(_mainKeys, StringComparer.Ordinal);
				}

				foreach (string file in files.Keys.Where(file => Matches(full, file)).Order(StringComparer.Ordinal))
				{
					Read(file, depth + 1);
				}
			}
		}
	}
}
