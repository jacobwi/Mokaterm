namespace Mokaterm.Abstractions.Terminal;

/// <summary>
/// Output kept up to a byte cap can end inside a UTF-8 character, and the half that made it would only decode as a
/// replacement mark. This finds where the last whole character ends.
/// </summary>
public static class Utf8Cut
{
	/// <summary>How much of <paramref name="bytes"/> ends on a whole UTF-8 character.</summary>
	public static int CompleteLength(ReadOnlySpan<byte> bytes)
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

			// Not a lead byte at all: the bytes are not UTF-8 there, so there is nothing to keep whole.
			_ => 1,
		};

		return bytes.Length - start < needed ? start : bytes.Length;
	}
}
