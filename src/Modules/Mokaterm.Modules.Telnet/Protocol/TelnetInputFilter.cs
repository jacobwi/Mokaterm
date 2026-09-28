namespace Mokaterm.Modules.Telnet.Protocol;

/// <summary>
/// Applies the one NVT rule that survives IAC unescaping: outside binary mode a carriage return that means "column
/// one and stay on this line" travels as CR NUL, and the terminal should see only the CR.
/// </summary>
internal sealed class TelnetInputFilter
{
	private const byte CarriageReturn = 0x0D;

	private bool _afterCarriageReturn;

	/// <summary>Compacts <paramref name="data"/> in place and returns how many bytes are left.</summary>
	public int Filter(Span<byte> data, bool binary)
	{
		if (binary)
		{
			// An 8 bit clean connection carries a NUL that means NUL.
			_afterCarriageReturn = false;
			return data.Length;
		}

		int written = 0;
		for (int read = 0; read < data.Length; read++)
		{
			byte value = data[read];
			if (_afterCarriageReturn && value == 0)
			{
				_afterCarriageReturn = false;
				continue;
			}

			_afterCarriageReturn = value == CarriageReturn;
			data[written++] = value;
		}

		return written;
	}
}
