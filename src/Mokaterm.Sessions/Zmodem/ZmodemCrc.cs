namespace Mokaterm.Sessions.Zmodem;

/// <summary>
/// ZMODEM's two frame checks: CRC-16 (CCITT polynomial 0x1021, starting at 0, finished by two zero bytes) for hex
/// headers and 16-bit binary frames, and the zip-style CRC-32 for 32-bit binary frames, complemented on the wire so a
/// receiver that folds the check into its running value ends at a known constant.
/// </summary>
internal static class ZmodemCrc
{
	/// <summary>The value a ZMODEM CRC-32 has after the data plus its own four check bytes, shifted in low byte first.</summary>
	public const uint Verified32 = 0xDEBB20E3;

	private static readonly ushort[] Table16 = BuildTable16();
	private static readonly uint[] Table32 = BuildTable32();

	/// <summary>The running CRC-16 of <paramref name="data"/>, starting where a sender starts.</summary>
	public static ushort Running16(ReadOnlySpan<byte> data)
	{
		ushort crc = 0;
		foreach (byte value in data)
		{
			crc = Append16(crc, value);
		}

		return crc;
	}

	/// <summary>The CRC-16 of <paramref name="data"/> as a sender transmits it: fed, then finished with two zero bytes.</summary>
	public static ushort Crc16(ReadOnlySpan<byte> data) => Finish16(Running16(data));

	/// <summary>
	/// lrzsz's <c>updcrc</c>: the table is read at the register's high byte and the data byte joins after it, which is this
	/// family's own convention — not the one that indexes the table by high byte XOR data.
	/// </summary>
	public static ushort Append16(ushort crc, byte value) => (ushort)((ushort)(crc << 8) ^ Table16[(crc >> 8) & 0xFF] ^ value);

	/// <summary>The finish of a CRC-16 over already-fed bytes: the polynomial applied as if two zero bytes followed.</summary>
	public static ushort Finish16(ushort crc) => (ushort)(Append16(Append16(crc, 0), 0));

	/// <summary>The running CRC-32 of <paramref name="data"/>, starting where a sender starts.</summary>
	public static uint Running32(ReadOnlySpan<byte> data)
	{
		uint crc = 0xFFFFFFFF;
		foreach (byte value in data)
		{
			crc = Append32(crc, value);
		}

		return crc;
	}

	public static uint Append32(uint crc, byte value) => (crc >> 8) ^ Table32[(crc ^ value) & 0xFF];

	/// <summary>What goes on the wire after 32-bit-checked data: the complement of the running value.</summary>
	public static uint Transmit32(ReadOnlySpan<byte> data) => ~Running32(data);

	private static ushort[] BuildTable16()
	{
		ushort[] table = new ushort[256];
		for (int value = 0; value < table.Length; value++)
		{
			ushort entry = (ushort)(value << 8);
			for (int bit = 0; bit < 8; bit++)
			{
				entry = (entry & 0x8000) != 0 ? (ushort)((entry << 1) ^ 0x1021) : (ushort)(entry << 1);
			}

			table[value] = entry;
		}

		return table;
	}

	private static uint[] BuildTable32()
	{
		uint[] table = new uint[256];
		for (int value = 0; value < table.Length; value++)
		{
			uint entry = (uint)value;
			for (int bit = 0; bit < 8; bit++)
			{
				entry = (entry & 1) != 0 ? (entry >> 1) ^ 0xEDB88320 : entry >> 1;
			}

			table[value] = entry;
		}

		return table;
	}
}
