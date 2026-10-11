using System.Text;

namespace Mokaterm.Modules.Ssh.Admin;

/// <summary>Output text made fit for a static monospace box.</summary>
internal static class AdminText
{
	/// <summary>
	/// Drops escape sequences and control characters other than tabs and line breaks. The scripts run without a
	/// terminal so tools rarely colour anything, but a status line or a log file can still carry them.
	/// </summary>
	public static string Clean(string text)
	{
		ArgumentNullException.ThrowIfNull(text);
		if (!NeedsCleaning(text))
		{
			return text;
		}

		StringBuilder builder = new(text.Length);
		for (int index = 0; index < text.Length; index++)
		{
			char character = text[index];
			if (character == '\u001b')
			{
				index = SkipEscape(text, index);
				continue;
			}

			if (character is '\t' or '\n' || !char.IsControl(character))
			{
				builder.Append(character);
			}
		}

		return builder.ToString();
	}

	private static bool NeedsCleaning(string text)
	{
		foreach (char character in text)
		{
			if (char.IsControl(character) && character is not ('\t' or '\n'))
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>The index of the last character of the sequence that starts at <paramref name="start"/>.</summary>
	private static int SkipEscape(string text, int start)
	{
		if (start + 1 >= text.Length)
		{
			return start;
		}

		char kind = text[start + 1];
		if (kind == '[')
		{
			// CSI: parameters and intermediates, then one final byte from @ to ~.
			int index = start + 2;
			while (index < text.Length && text[index] is < '@' or > '~')
			{
				index++;
			}

			return Math.Min(index, text.Length - 1);
		}

		if (kind == ']')
		{
			// OSC: up to BEL or ESC backslash.
			int index = start + 2;
			while (index < text.Length)
			{
				if (text[index] == '\u0007')
				{
					return index;
				}

				if (text[index] == '\u001b' && index + 1 < text.Length && text[index + 1] == '\\')
				{
					return index + 1;
				}

				index++;
			}

			return text.Length - 1;
		}

		return start + 1;
	}
}
