using System.Globalization;

namespace Mokaterm.Sessions.Zmodem;

/// <summary>
/// The file information subpacket that follows a ZFILE header: the path, then the length (decimal), the modification
/// time (octal seconds since 1970), the file mode (octal) and optional estimates, each field separated by a space and the
/// whole string closed by a null. sz sends them all; a receiver takes what is there.
/// </summary>
internal sealed record ZmodemFileInfo(string Name, long? Length, DateTimeOffset? Modified, int? Mode)
{
	public static ZmodemFileInfo Parse(ReadOnlySpan<byte> data)
	{
		int separator = data.IndexOf((byte)0);
		if (separator < 0)
		{
			// Without the null there is no name to take, so nothing about the file can be trusted.
			return new ZmodemFileInfo("", null, null, null);
		}

		string name = System.Text.Encoding.UTF8.GetString(data[..separator]);
		string rest = System.Text.Encoding.UTF8.GetString(data[(separator + 1)..]).TrimEnd('\0');
		string[] fields = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);

		long? length = fields.Length > 0
			&& long.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsedLength)
			? parsedLength
			: null;
		DateTimeOffset? modified = fields.Length > 1 && TryParseOctal(fields[1], out long parsedTime) && parsedTime > 0
			? DateTimeOffset.FromUnixTimeSeconds(parsedTime)
			: null;
		int? mode = fields.Length > 2 && TryParseOctal(fields[2], out long parsedMode) ? (int)parsedMode : null;
		return new ZmodemFileInfo(name, length, modified, mode);
	}

	/// <summary>The subpacket bytes for a file this side sends: name, then length, octal mtime and octal mode, closed by a null.</summary>
	public byte[] ToSubpacket()
	{
		string info = string.Create(
			CultureInfo.InvariantCulture,
			$"{Name}\0{Length ?? 0} {ToOctal(Modified?.ToUnixTimeSeconds() ?? 0)} {ToOctal(Mode ?? 0)} 0 0 0");
		return [.. System.Text.Encoding.UTF8.GetBytes(info), 0];
	}

	private static string ToOctal(long value) => Convert.ToString(value, 8);

	/// <summary>Parses the octal numbers the protocol puts in its file information, the way sscanf's %lo does.</summary>
	private static bool TryParseOctal(string text, out long value)
	{
		value = 0;
		if (text.Length == 0)
		{
			return false;
		}

		foreach (char digit in text)
		{
			if (digit is < '0' or > '7')
			{
				return false;
			}

			try
			{
				value = checked((value * 8) + (digit - '0'));
			}
			catch (OverflowException)
			{
				return false;
			}
		}

		return true;
	}
}
