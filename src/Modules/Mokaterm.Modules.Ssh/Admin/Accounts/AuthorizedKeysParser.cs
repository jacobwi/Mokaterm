using Mokaterm.Modules.Ssh.Connection;

namespace Mokaterm.Modules.Ssh.Admin.Accounts;

/// <summary>
/// Reads <c>authorized_keys</c> the way sshd does: a line is a key, or options and then a key, and anything that is
/// neither (a comment, a blank line, a line sshd would skip) is left alone. A key counts only when its base64 decodes
/// and the type written inside it matches the type the line names.
/// </summary>
internal static class AuthorizedKeysParser
{
	/// <summary>Every key line, in file order. Lines that hold no key are skipped, never changed.</summary>
	public static IReadOnlyList<AuthorizedKey> Parse(string? text)
	{
		List<AuthorizedKey> keys = [];
		if (string.IsNullOrEmpty(text))
		{
			return keys;
		}

		string[] lines = text.Split('\n');
		for (int index = 0; index < lines.Length; index++)
		{
			if (ParseLine(lines[index], index + 1) is { } key)
			{
				keys.Add(key);
			}
		}

		return keys;
	}

	/// <summary>One line, or null when it holds no key sshd would accept.</summary>
	public static AuthorizedKey? ParseLine(string line, int lineNumber)
	{
		ArgumentNullException.ThrowIfNull(line);
		string trimmed = line.Trim();
		if (trimmed.Length == 0 || trimmed[0] == '#')
		{
			return null;
		}

		// sshd tries the line as a bare key first and only then as options followed by a key.
		if (ParseKey(trimmed, "", lineNumber) is { } bare)
		{
			return bare;
		}

		int optionsEnd = OptionsEnd(trimmed);
		return optionsEnd <= 0 || optionsEnd >= trimmed.Length
			? null
			: ParseKey(trimmed[optionsEnd..].TrimStart(), trimmed[..optionsEnd], lineNumber);
	}

	/// <summary>The key's base64 when it decodes to a blob whose own type is <paramref name="algorithm"/>, else null.</summary>
	public static byte[]? DecodeBlob(string algorithm, string blob)
	{
		ArgumentNullException.ThrowIfNull(algorithm);
		ArgumentNullException.ThrowIfNull(blob);
		if (blob.Length == 0 || blob.Length > 16 * 1024)
		{
			return null;
		}

		byte[] bytes;
		try
		{
			bytes = Convert.FromBase64String(blob);
		}
		catch (FormatException)
		{
			return null;
		}

		return SshHostKeys.ReadKeyType(bytes) == algorithm ? bytes : null;
	}

	private static AuthorizedKey? ParseKey(string text, string options, int lineNumber)
	{
		string[] words = text.Split((char[])[' ', '\t'], 3, StringSplitOptions.RemoveEmptyEntries);
		if (words.Length < 2 || DecodeBlob(words[0], words[1]) is not { } blob)
		{
			return null;
		}

		return new AuthorizedKey
		{
			LineNumber = lineNumber,
			Options = options,
			Algorithm = words[0],
			Blob = words[1],
			Comment = words.Length > 2 ? words[2].Trim() : "",
			Fingerprint = SshHostKeys.Fingerprint(blob),
		};
	}

	/// <summary>Where the options end: at the first space or tab outside double quotes, where a backslash escapes a quote.</summary>
	private static int OptionsEnd(string line)
	{
		bool quoted = false;
		for (int index = 0; index < line.Length; index++)
		{
			char character = line[index];
			if (quoted && character == '\\' && index + 1 < line.Length && line[index + 1] == '"')
			{
				index++;
			}
			else if (character == '"')
			{
				quoted = !quoted;
			}
			else if (!quoted && character is ' ' or '\t')
			{
				return index;
			}
		}

		return -1;
	}
}
