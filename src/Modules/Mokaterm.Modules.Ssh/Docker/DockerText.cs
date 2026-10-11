using System.Text;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// What a docker command printed, as text for the screen. JSON is read as it came; log lines and change output lose
/// their escape sequences and control characters, because colours and cursor moves mean nothing in a static box and a
/// container's own output can hold anything at all.
/// </summary>
internal static class DockerText
{
	private const char Escape = (char)0x1B;
	private const char Bell = (char)0x07;

	/// <summary>UTF-8 text. When the output was cut, a character the cut split in half is dropped instead of shown as a replacement mark.</summary>
	public static string Decode(ReadOnlySpan<byte> bytes, bool truncated)
	{
		if (truncated)
		{
			bytes = bytes[..CompleteLength(bytes)];
		}

		return Encoding.UTF8.GetString(bytes);
	}

	/// <summary>
	/// <paramref name="text"/> without escape sequences (CSI, OSC and the two-character ones) and without control
	/// characters other than tabs and line feeds; a carriage return before a line feed goes too.
	/// </summary>
	public static string Plain(string text)
	{
		ArgumentNullException.ThrowIfNull(text);
		if (!text.Any(character => char.IsControl(character) && character is not ('\n' or '\t')))
		{
			return text;
		}

		StringBuilder plain = new(text.Length);
		int index = 0;
		while (index < text.Length)
		{
			char character = text[index];
			if (character == Escape)
			{
				index = SkipSequence(text, index + 1);
				continue;
			}

			if (character is '\n' or '\t' || !char.IsControl(character))
			{
				plain.Append(character);
			}

			index++;
		}

		return plain.ToString();
	}

	/// <summary>The first line that says something, trimmed: what a message quotes from stderr.</summary>
	public static string FirstLine(string? text)
	{
		foreach (string line in (text ?? "").Split('\n'))
		{
			string trimmed = Plain(line).Trim();
			if (trimmed.Length > 0)
			{
				return trimmed.Length > 300 ? trimmed[..300] + "..." : trimmed;
			}
		}

		return "";
	}

	/// <summary>Where the sequence that starts after an escape at <paramref name="start"/> ends.</summary>
	private static int SkipSequence(string text, int start)
	{
		if (start >= text.Length)
		{
			return start;
		}

		char kind = text[start];
		if (kind == '[')
		{
			// CSI: parameters and intermediates, then one final byte in @ to ~.
			int index = start + 1;
			while (index < text.Length && text[index] is < '@' or > '~')
			{
				index++;
			}

			return Math.Min(index + 1, text.Length);
		}

		if (kind is ']' or 'P' or 'X' or '^' or '_')
		{
			// A string sequence ends at BEL or at ESC backslash.
			int index = start + 1;
			while (index < text.Length)
			{
				if (text[index] == Bell)
				{
					return index + 1;
				}

				if (text[index] == Escape && index + 1 < text.Length && text[index + 1] == '\\')
				{
					return index + 2;
				}

				index++;
			}

			return text.Length;
		}

		// A character set designator takes one more character; everything else is the escape and one character.
		return kind is '(' or ')' or '*' or '+' ? Math.Min(start + 2, text.Length) : start + 1;
	}

	/// <summary>How much of <paramref name="bytes"/> ends on a whole UTF-8 character.</summary>
	private static int CompleteLength(ReadOnlySpan<byte> bytes)
	{
		int start = bytes.Length - 1;
		int continuations = 0;
		while (start >= 0 && continuations < 3 && (bytes[start] & 0xC0) == 0x80)
		{
			start--;
			continuations++;
		}

		if (start < 0)
		{
			return bytes.Length;
		}

		byte lead = bytes[start];
		int needed = lead switch
		{
			< 0x80 => 1,
			_ when (lead & 0xE0) == 0xC0 => 2,
			_ when (lead & 0xF0) == 0xE0 => 3,
			_ when (lead & 0xF8) == 0xF0 => 4,
			_ => 1,
		};

		return bytes.Length - start < needed ? start : bytes.Length;
	}
}
