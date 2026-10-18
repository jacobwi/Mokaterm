using System.Globalization;
using System.Text;

namespace Mokaterm.Core.Import;

/// <summary>
/// PuTTY and WinSCP percent-encode the characters a registry key or an ini section cannot hold, so a session
/// called <c>web/prod</c> is stored as <c>web%2Fprod</c>. WinSCP encodes its string values the same way, a path's
/// backslashes included, and marks text outside the ANSI code page by putting a UTF-8 byte order mark first.
/// </summary>
internal static class SessionNameCodec
{
	private const string Utf8Marker = "%EF%BB%BF";

	public static string Decode(string encoded)
	{
		if (string.IsNullOrEmpty(encoded) || !encoded.Contains('%', StringComparison.Ordinal))
		{
			return encoded;
		}

		if (encoded.StartsWith(Utf8Marker, StringComparison.OrdinalIgnoreCase))
		{
			return DecodeUtf8(encoded[Utf8Marker.Length..]);
		}

		StringBuilder builder = new(encoded.Length);
		for (int i = 0; i < encoded.Length; i++)
		{
			if (encoded[i] == '%'
				&& i + 2 < encoded.Length
				&& byte.TryParse(encoded.AsSpan(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte value))
			{
				builder.Append((char)value);
				i += 2;
				continue;
			}

			builder.Append(encoded[i]);
		}

		return builder.ToString();
	}

	private static string DecodeUtf8(string encoded)
	{
		List<byte> bytes = new(encoded.Length);
		for (int i = 0; i < encoded.Length; i++)
		{
			if (encoded[i] == '%'
				&& i + 2 < encoded.Length
				&& byte.TryParse(encoded.AsSpan(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte value))
			{
				bytes.Add(value);
				i += 2;
				continue;
			}

			bytes.AddRange(Encoding.UTF8.GetBytes(encoded[i].ToString()));
		}

		return Encoding.UTF8.GetString([.. bytes]);
	}
}
