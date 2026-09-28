using System.Globalization;

namespace Mokaterm.Core.Terminal;

/// <summary>Reads the color spellings other terminals use and writes them back as <c>#rrggbb</c> or <c>#rrggbbaa</c>.</summary>
internal static class TerminalColors
{
	/// <summary>Alpha for a selection color derived from the foreground, matching the built-in Moka themes.</summary>
	public const byte SelectionAlpha = 0x40;

	/// <summary>How far a missing bright color is lightened from its normal counterpart.</summary>
	public const double BrightLift = 0.25;

	// Below this relative luminance white text has more contrast on the color than black text, which is where a
	// background starts reading as dark.
	private const double DarkLuminance = 0.179;

	/// <summary>Normalizes one color, keeping alpha. Returns null when <paramref name="value"/> is not a color.</summary>
	public static string? Parse(string? value)
	{
		ReadOnlySpan<char> span = value.AsSpan().Trim().Trim('\'').Trim('"').Trim();
		if (span.IsEmpty)
		{
			return null;
		}

		if (span[0] == '#')
		{
			span = span[1..];
		}
		else if (span.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
		{
			span = span[2..];
		}
		else if (span.StartsWith("rgb:", StringComparison.OrdinalIgnoreCase))
		{
			return ParseXChannels(span[4..]);
		}

		return ParseHex(span);
	}

	/// <summary>Normalizes one color and drops any alpha, for the slots that must stay opaque.</summary>
	public static string? ParseOpaque(string? value)
	{
		string? color = Parse(value);
		return color is null ? null : WithAlpha(color, 255);
	}

	public static (byte Red, byte Green, byte Blue, byte Alpha) Channels(string color)
	{
		ReadOnlySpan<char> span = color.AsSpan(1);
		return (Hex(span[..2]), Hex(span[2..4]), Hex(span[4..6]), span.Length >= 8 ? Hex(span[6..8]) : (byte)255);
	}

	public static string FromChannels(byte red, byte green, byte blue, byte alpha = 255) =>
		alpha == 255
			? string.Create(CultureInfo.InvariantCulture, $"#{red:x2}{green:x2}{blue:x2}")
			: string.Create(CultureInfo.InvariantCulture, $"#{red:x2}{green:x2}{blue:x2}{alpha:x2}");

	public static string WithAlpha(string color, byte alpha)
	{
		(byte red, byte green, byte blue, _) = Channels(color);
		return FromChannels(red, green, blue, alpha);
	}

	/// <summary>Flattens <paramref name="color"/> onto <paramref name="background"/>, for formats without alpha.</summary>
	public static string Flatten(string color, string background)
	{
		(byte red, byte green, byte blue, byte alpha) = Channels(color);
		if (alpha == 255)
		{
			return color;
		}

		(byte backRed, byte backGreen, byte backBlue, _) = Channels(background);
		return FromChannels(Mix(red, backRed, alpha), Mix(green, backGreen, alpha), Mix(blue, backBlue, alpha));
	}

	/// <summary>
	/// Raises the lightness of <paramref name="color"/> by <paramref name="amount"/> of the way to white, keeping its
	/// hue and saturation so a derived bright red still reads as red.
	/// </summary>
	public static string Lighten(string color, double amount)
	{
		(byte red, byte green, byte blue, byte alpha) = Channels(color);
		double r = red / 255.0;
		double g = green / 255.0;
		double b = blue / 255.0;
		double high = Math.Max(r, Math.Max(g, b));
		double low = Math.Min(r, Math.Min(g, b));
		double lightness = (high + low) / 2;
		double target = lightness + ((1 - lightness) * amount);
		if (high <= low)
		{
			return FromChannels(Byte(target), Byte(target), Byte(target), alpha);
		}

		// HSL keeps the chroma proportional to how far the lightness is from black or white.
		double scale = (1 - Math.Abs((2 * target) - 1)) / (1 - Math.Abs((2 * lightness) - 1));
		return FromChannels(
			Byte(target + ((r - lightness) * scale)),
			Byte(target + ((g - lightness) * scale)),
			Byte(target + ((b - lightness) * scale)),
			alpha);
	}

	public static bool IsDark(string background) => Luminance(background) < DarkLuminance;

	public static double Luminance(string color)
	{
		(byte red, byte green, byte blue, _) = Channels(color);
		return (0.2126 * Linear(red)) + (0.7152 * Linear(green)) + (0.0722 * Linear(blue));
	}

	private static string? ParseHex(ReadOnlySpan<char> span)
	{
		foreach (char character in span)
		{
			if (!char.IsAsciiHexDigit(character))
			{
				return null;
			}
		}

		return span.Length switch
		{
			3 => FromChannels(Nibble(span[0]), Nibble(span[1]), Nibble(span[2])),
			4 => FromChannels(Nibble(span[0]), Nibble(span[1]), Nibble(span[2]), Nibble(span[3])),
			6 => FromChannels(Hex(span[..2]), Hex(span[2..4]), Hex(span[4..6])),
			8 => FromChannels(Hex(span[..2]), Hex(span[2..4]), Hex(span[4..6]), Hex(span[6..8])),
			_ => null,
		};
	}

	// X11 writes rgb:rr/gg/bb, and each channel may be 1 to 4 hex digits wide.
	private static string? ParseXChannels(ReadOnlySpan<char> span)
	{
		Span<byte> channels = stackalloc byte[3];
		for (int index = 0; index < 3; index++)
		{
			int separator = span.IndexOf('/');
			ReadOnlySpan<char> part = separator < 0 ? span : span[..separator];
			span = separator < 0 ? [] : span[(separator + 1)..];
			if (part.Length is 0 or > 4 || (index < 2 && separator < 0))
			{
				return null;
			}

			int value = 0;
			foreach (char character in part)
			{
				if (!char.IsAsciiHexDigit(character))
				{
					return null;
				}

				value = (value << 4) | Nibble(character);
			}

			int maximum = (1 << (4 * part.Length)) - 1;
			channels[index] = (byte)Math.Round(value * 255.0 / maximum);
		}

		return span.IsEmpty ? FromChannels(channels[0], channels[1], channels[2]) : null;
	}

	private static byte Hex(ReadOnlySpan<char> pair) => (byte)((Nibble(pair[0]) << 4) | Nibble(pair[1]));

	private static byte Nibble(char character) =>
		(byte)(char.IsAsciiDigit(character) ? character - '0' : (character | 0x20) - 'a' + 10);

	private static byte Mix(byte color, byte background, byte alpha) =>
		(byte)Math.Round(((color * alpha) + (background * (255 - alpha))) / 255.0);

	private static byte Byte(double value) => (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);

	private static double Linear(byte channel)
	{
		double value = channel / 255.0;
		return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
	}
}
