using System.Globalization;

namespace Mokaterm.DevHost.Demo.Vnc;

/// <summary>Turns the X keysyms a viewer sends into the short labels the demo screen shows.</summary>
internal static class DemoKeys
{
	private static readonly Dictionary<uint, string> Named = new()
	{
		[0xFF08] = "BACKSPACE",
		[0xFF09] = "TAB",
		[0xFF0D] = "ENTER",
		[0xFF1B] = "ESC",
		[0xFF50] = "HOME",
		[0xFF51] = "LEFT",
		[0xFF52] = "UP",
		[0xFF53] = "RIGHT",
		[0xFF54] = "DOWN",
		[0xFF55] = "PAGE UP",
		[0xFF56] = "PAGE DOWN",
		[0xFF57] = "END",
		[0xFF61] = "PRINT",
		[0xFF63] = "INSERT",
		[0xFFFF] = "DELETE",
		[0xFFE1] = "SHIFT",
		[0xFFE3] = "CTRL",
		[0xFFE9] = "ALT",
		[0xFFEB] = "SUPER",
		[0xFFE5] = "CAPS LOCK",
	};

	public static string Describe(uint keysym)
	{
		if (Named.TryGetValue(keysym, out string? name))
		{
			return name;
		}

		if (keysym is >= 0xFFBE and <= 0xFFC9)
		{
			return string.Create(CultureInfo.InvariantCulture, $"F{keysym - 0xFFBD}");
		}

		if (keysym == ' ')
		{
			return "SPACE";
		}

		if (keysym is >= 0x21 and <= 0x7E)
		{
			return char.ToUpperInvariant((char)keysym).ToString();
		}

		return string.Create(CultureInfo.InvariantCulture, $"0X{keysym:X}");
	}
}
