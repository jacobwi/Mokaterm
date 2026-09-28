using System.Globalization;
using System.Text;

namespace Mokaterm.Core.Import;

/// <summary>
/// PuTTY and WinSCP percent-encode the characters a registry key or an ini section cannot hold, so a session
/// called <c>web/prod</c> is stored as <c>web%2Fprod</c>.
/// </summary>
internal static class SessionNameCodec
{
	public static string Decode(string encoded)
	{
		if (string.IsNullOrEmpty(encoded) || !encoded.Contains('%', StringComparison.Ordinal))
		{
			return encoded;
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
}
