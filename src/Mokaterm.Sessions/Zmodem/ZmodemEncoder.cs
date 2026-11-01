namespace Mokaterm.Sessions.Zmodem;

/// <summary>
/// Writes ZMODEM frames onto a byte buffer the caller sends to the other side. The escaping is lrzsz's: ZDLE, XON, XOFF
/// and DLE (with or without the eighth bit) always, and CR only right after an '@' so a Telenet-style link cannot read
/// it as a command escape.
/// </summary>
internal sealed class ZmodemEncoder
{
	private readonly List<byte> _output = [];

	/// <summary>The last data byte written, escaped or not, which decides whether a following CR needs hiding.</summary>
	private int _lastRaw = -1;

	/// <summary>Writes bytes untouched by the framing, such as the "OO" that closes a session.</summary>
	public void Raw(byte value) => _output.Add(value);

	public bool HasOutput => _output.Count > 0;

	/// <summary>Takes everything written so far and clears the buffer.</summary>
	public byte[] TakeOutput()
	{
		if (_output.Count == 0)
		{
			return [];
		}

		byte[] taken = [.. _output];
		_output.Clear();
		return taken;
	}

	public void Header(ZmodemHeader header) => Header(header, header.Encoding);

	/// <summary>Writes a header in the given encoding, whatever the parsed header arrived as.</summary>
	public void Header(ZmodemHeader header, ZmodemHeaderEncoding encoding)
	{
		switch (encoding)
		{
			case ZmodemHeaderEncoding.Hex:
				WriteHexHeader(header);
				break;
			case ZmodemHeaderEncoding.Binary16:
				WriteBinary16Header(header);
				break;
			default:
				WriteBinary32Header(header);
				break;
		}
	}

	/// <summary>
	/// Writes a data subpacket: <paramref name="data"/> escaped, then ZDLE and <paramref name="frameEnd"/>, then the
	/// frame check over both. A ZCRCW subpacket ends with XON, as the protocol specifies, to release flow control.
	/// </summary>
	public void Subpacket(ReadOnlySpan<byte> data, byte frameEnd, bool crc32)
	{
		if (crc32)
		{
			uint crc = 0xFFFFFFFF;
			foreach (byte value in data)
			{
				crc = ZmodemCrc.Append32(crc, value);
				EscapeAndWrite(value);
			}

			crc = ZmodemCrc.Append32(crc, frameEnd);
			_output.Add(ZmodemCode.ZDLE);
			_output.Add(frameEnd);
			uint check = ~crc;
			for (int index = 0; index < 4; index++)
			{
				EscapeAndWrite((byte)check);
				check >>= 8;
			}
		}
		else
		{
			ushort crc = 0;
			foreach (byte value in data)
			{
				crc = ZmodemCrc.Append16(crc, value);
				EscapeAndWrite(value);
			}

			crc = ZmodemCrc.Append16(crc, frameEnd);
			_output.Add(ZmodemCode.ZDLE);
			_output.Add(frameEnd);
			crc = ZmodemCrc.Finish16(crc);
			EscapeAndWrite((byte)(crc >> 8));
			EscapeAndWrite((byte)crc);
		}

		if (frameEnd == ZmodemCode.ZCRCW)
		{
			_output.Add(ZmodemCode.XON);
		}
	}

	/// <summary>
	/// ZMODEM's cancel sequence, the way lrzsz sends it: a ZCAN hex header, then eight CANs so the other side's five-CAN
	/// detector fires, then backspaces that erase the CANs from a shell prompt's line if one is listening.
	/// </summary>
	public void CancelSequence()
	{
		Header(new ZmodemHeader(ZmodemCode.ZCAN));
		for (int index = 0; index < 8; index++)
		{
			_output.Add(ZmodemCode.Cancel);
		}

		for (int index = 0; index < 8; index++)
		{
			_output.Add(8);
		}
	}

	private void WriteHexHeader(ZmodemHeader header)
	{
		byte[] body = [header.Type, header.B0, header.B1, header.B2, header.B3];
		ushort crc = ZmodemCrc.Crc16(body);

		_output.Add(ZmodemCode.ZPAD);
		_output.Add(ZmodemCode.ZPAD);
		_output.Add(ZmodemCode.ZDLE);
		_output.Add(ZmodemCode.ZHEX);
		WriteHex(body[0]);
		WriteHex(body[1]);
		WriteHex(body[2]);
		WriteHex(body[3]);
		WriteHex(body[4]);
		WriteHex((byte)(crc >> 8));
		WriteHex((byte)crc);

		// ZACK and ZFIN go without XON so an in-flight sender keeps its flow control and the session can close clean.
		_output.Add((byte)'\r');
		_output.Add((byte)'\n');
		if (header.Type is not (ZmodemCode.ZACK or ZmodemCode.ZFIN))
		{
			_output.Add(ZmodemCode.XON);
		}
	}

	private void WriteBinary16Header(ZmodemHeader header)
	{
		_output.Add(ZmodemCode.ZPAD);
		_output.Add(ZmodemCode.ZDLE);
		_output.Add(ZmodemCode.ZBIN);
		ushort crc = WriteEscapedBody(header, ZmodemCrc.Append16);
		EscapeAndWrite((byte)(crc >> 8));
		EscapeAndWrite((byte)crc);
	}

	private void WriteBinary32Header(ZmodemHeader header)
	{
		_output.Add(ZmodemCode.ZPAD);
		_output.Add(ZmodemCode.ZPAD);
		_output.Add(ZmodemCode.ZDLE);
		_output.Add(ZmodemCode.ZBIN32);
		uint crc = 0xFFFFFFFF;
		crc = ZmodemCrc.Append32(crc, header.Type);
		EscapeAndWrite(header.Type);
		crc = ZmodemCrc.Append32(crc, header.B0);
		EscapeAndWrite(header.B0);
		crc = ZmodemCrc.Append32(crc, header.B1);
		EscapeAndWrite(header.B1);
		crc = ZmodemCrc.Append32(crc, header.B2);
		EscapeAndWrite(header.B2);
		crc = ZmodemCrc.Append32(crc, header.B3);
		EscapeAndWrite(header.B3);
		uint check = ~crc;
		for (int index = 0; index < 4; index++)
		{
			EscapeAndWrite((byte)check);
			check >>= 8;
		}
	}

	private ushort WriteEscapedBody(ZmodemHeader header, Func<ushort, byte, ushort> append)
	{
		ushort crc = 0;
		crc = append(crc, header.Type);
		EscapeAndWrite(header.Type);
		crc = append(crc, header.B0);
		EscapeAndWrite(header.B0);
		crc = append(crc, header.B1);
		EscapeAndWrite(header.B1);
		crc = append(crc, header.B2);
		EscapeAndWrite(header.B2);
		crc = append(crc, header.B3);
		EscapeAndWrite(header.B3);
		return ZmodemCrc.Finish16(crc);
	}

	private void WriteHex(byte value)
	{
		_output.Add((byte)ZmodemCode.HexDigits[value >> 4]);
		_output.Add((byte)ZmodemCode.HexDigits[value & 0x0F]);
	}

	private void EscapeAndWrite(byte value)
	{
		bool escaped = NeedsEscape(value)
			// CR only needs hiding from a link that would read '@' CR as a command escape; the byte before it decides.
			|| (value is ((byte)'\r' or 0x8D) && (_lastRaw & 0x7F) == (byte)'@');
		_lastRaw = value;
		if (escaped)
		{
			_output.Add(ZmodemCode.ZDLE);
			_output.Add((byte)(value ^ 0x40));
			return;
		}

		_output.Add(value);
	}

	private static bool NeedsEscape(byte value) => value is (ZmodemCode.ZDLE or ZmodemCode.XON or ZmodemCode.XOFF or 0x10 or 0x90 or 0x91 or 0x93);
}
